import pandas as pd
import numpy as np
import os

# ==========================================
# FILE & DIRECTORY SETTINGS
# ==========================================
INPUT_DIR  = "../grouped_cleaned_output/grouped_by_condition"
OUTPUT_DIR = "../extracted_trial_metrics"
CONDITION_DIR = os.path.join(OUTPUT_DIR, "conditions")
COMBINED_FILENAME = "logged_trial_metrics_combined.csv"
CONDITION_ORDER = ['Baseline', 'HapticsFixed', 'HapticsAdaptive']

DATA_CONFIG = {
    'Baseline': {'in': 'Baseline.csv', 'out': 'logged_baseline_metrics.csv'},
    'HapticsFixed': {'in': 'HapticsFixed.csv', 'out': 'logged_haptics_fixed_metrics.csv'},
    'HapticsAdaptive': {'in': 'HapticsAdaptive.csv', 'out': 'logged_haptics_adaptive_metrics.csv'}
}

def calculate_secondary_task_performance(trial_data, response_window=1.5):
    """Calculates n-Back performance with Stimulus-based Debounce."""
    trial_data = trial_data.sort_values('Timestamp').copy()
    for col in ['SecTaskNum', 'SecTaskIsTarget', 'SecTaskPressed']:
        if col in trial_data.columns:
            trial_data[col] = pd.to_numeric(trial_data[col], errors='coerce')

    trial_data['stim_block'] = (trial_data['SecTaskNum'] != trial_data['SecTaskNum'].shift()).cumsum()
    ts, is_target, pressed = trial_data['Timestamp'].values, trial_data['SecTaskIsTarget'].values, trial_data['SecTaskPressed'].values
    valid_mask = (is_target != -1)
    stim_change = (trial_data['stim_block'] != trial_data['stim_block'].shift())
    stim_change.iloc[0] = True
    
    target_times = ts[stim_change & (is_target == 1) & valid_mask]
    distractor_times = ts[stim_change & (is_target == 0) & valid_mask]
    press_onsets_mask = (pressed == 1) & (np.roll(pressed, 1) == 0)
    if len(pressed) > 0 and pressed[0] == 1: press_onsets_mask[0] = True
    
    press_times = trial_data[press_onsets_mask].drop_duplicates(subset=['stim_block'])['Timestamp'].values
    hits, reaction_times, used_press_indices = 0, [], set()
    for t_start in target_times:
        t_end = t_start + response_window
        for i, p_time in enumerate(press_times):
            if i not in used_press_indices and t_start <= p_time <= t_end:
                hits += 1
                reaction_times.append(p_time - t_start)
                used_press_indices.add(i)
                break

    num_targets, num_distractors = len(target_times), len(distractor_times)
    hit_rate = (hits / num_targets * 100) if num_targets > 0 else 0
    fa_count = len(press_times) - len(used_press_indices)
    fa_rate = (fa_count / num_distractors * 100) if num_distractors > 0 else 0
    
    return {
        'nback_hitrate_pct': hit_rate,
        'nback_farate_pct': fa_rate,
        'nback_accuracy_corrected_pct': hit_rate - fa_rate,
        'nback_reactiontime_mean_seconds': np.mean(reaction_times) if reaction_times else np.nan
    }

# ==========================================
# TRIAL METRIC AGGREGATION
# ==========================================

def calculate_trial_metrics(trial_data, participant_id, trial_order, condition):
    if trial_data.empty: return None
    
    # Ensure all required columns are numeric
    metric_cols = ['Timestamp', 'Speed_KmH', 'LkaNormEffort', 'LKA_Switch', 'IsOnStraight',
                   'TrackCurvature_1/Meters', 'Haptic_L_Norm', 'Haptic_R_Norm', 
                   'CTE_Meters', 'B_HeadingError_Deg', 'SteerAngle', 'LKA_Engaged']
    
    for col in metric_cols:
        if col in trial_data.columns:
            trial_data[col] = pd.to_numeric(trial_data[col], errors='coerce')

    results = {'participant_id': participant_id, 'trial_order': trial_order, 'condition': condition}

    # --- 1. TRACK SEGMENTATION LOGIC ---
    trial_data['radius'] = np.where(trial_data['TrackCurvature_1/Meters'] != 0, 
                                    1 / trial_data['TrackCurvature_1/Meters'].abs(), np.nan)
    trial_data['segment_type'] = 'straight'
    curves_mask = trial_data['IsOnStraight'] == 0
    if curves_mask.any():
        trial_data['curve_id'] = (trial_data['IsOnStraight'].diff() != 0).cumsum()
        def classify_curve(group):
            return 'short_turn' if group['radius'].median() < 20 else 'long_turn'
        curve_mapping = trial_data[curves_mask].groupby('curve_id').apply(classify_curve, include_groups=False).to_dict()
        trial_data.loc[curves_mask, 'segment_type'] = trial_data.loc[curves_mask, 'curve_id'].map(curve_mapping)

    # --- 2. OVERALL DRIVING PERFORMANCE ---
    results['laptime_total_seconds'] = trial_data['Timestamp'].max() - trial_data['Timestamp'].min()
    results['speed_mean_kmh'] = trial_data['Speed_KmH'].mean()
    results['speed_sd_kmh'] = trial_data['Speed_KmH'].std()
    results['cte_absmean_meters'] = trial_data['CTE_Meters'].abs().mean()
    results['cte_sd_meters'] = trial_data['CTE_Meters'].std()
    results['headingerror_absmean_deg'] = trial_data['B_HeadingError_Deg'].abs().mean()
    results['headingerror_sd_deg'] = trial_data['B_HeadingError_Deg'].std()
    results['steeringangle_sd_deg'] = trial_data['SteerAngle'].std()
    
    # --- 3. SYSTEM ENGAGEMENT (LKA & VIBRATION) ---
    results['lka_switch_on_pct_overall'] = trial_data['LKA_Switch'].mean() * 100
    
    # CALCULATE OVERALL ACTIVATION (Un-categorized)
    # This looks at the whole trial: "How often did LKA work when the switch was ON?"
    switched_on = trial_data[trial_data['LKA_Switch'] == 1]
    if not switched_on.empty:
        results['lka_active_while_switched_on_pct_overall'] = switched_on['LKA_Engaged'].mean() * 100
    else:
        results['lka_active_while_switched_on_pct_overall'] = 0.0

    lka_on = trial_data[trial_data['LKA_Engaged'] == 1]
    results['lka_effort_absmean_overall'] = lka_on['LkaNormEffort'].abs().mean() if not lka_on.empty else 0.0
    
    for seg in ['straight', 'short_turn', 'long_turn']:
        seg_data = trial_data[trial_data['segment_type'] == seg]
        if not seg_data.empty:
            # Switch usage
            results[f'switch_pct_{seg}'] = seg_data['LKA_Switch'].mean() * 100
            
            # YOUR NEW METRIC (Categorized)
            sw_on_seg = seg_data[seg_data['LKA_Switch'] == 1]
            results[f'lka_active_while_switched_on_pct_{seg}'] = sw_on_seg['LKA_Engaged'].mean() * 100 if not sw_on_seg.empty else 0.0

            # LKA Effort in segment
            seg_lka_on = seg_data[seg_data['LKA_Engaged'] == 1]
            results[f'lka_effort_absmean_{seg}'] = seg_lka_on['LkaNormEffort'].abs().mean() if not seg_lka_on.empty else 0.0
            
            # Vibration
            results[f'vibration_intensity_both_{seg}'] = (seg_data['Haptic_L_Norm'].abs() + seg_data['Haptic_R_Norm'].abs()).mean()
            seg_vib_active = (seg_data['Haptic_L_Norm'] > 0) | (seg_data['Haptic_R_Norm'] > 0)
            results[f'vibration_engaged_pct_{seg}'] = seg_vib_active.mean() * 100
            
            # Lateral performance
            results[f'cte_absmean_{seg}'] = seg_data['CTE_Meters'].abs().mean()
        else:
            # If segment doesn't exist in this lap, mark as NaN
            results[f'switch_pct_{seg}'] = np.nan
            results[f'lka_active_while_switched_on_pct_{seg}'] = np.nan
            results[f'lka_effort_absmean_{seg}'] = np.nan
            results[f'cte_absmean_{seg}'] = np.nan

    # --- 5. SECONDARY TASK ---
    results.update(calculate_secondary_task_performance(trial_data))
    return results

# ==========================================
# MAIN EXECUTION
# ==========================================

def main():
    # Ensure both the main directory and the subdirectory exist
    if not os.path.exists(CONDITION_DIR): 
        os.makedirs(CONDITION_DIR)
        
    all_metrics = []
    
    for cond_key, files in DATA_CONFIG.items():
        file_path = os.path.join(INPUT_DIR, files['in'])
        if not os.path.exists(file_path): 
            print(f"Skipping {cond_key}: File not found.")
            continue
            
        print(f"Processing {cond_key}...")
        raw_data = pd.read_csv(file_path, sep=';', decimal=',')
        
        grouped = raw_data.groupby(['participant_id', 'trial_order'])
        condition_list = [calculate_trial_metrics(tg, pid, order, cond_key) 
                          for (pid, order), tg in grouped]
        
        condition_list = [m for m in condition_list if m is not None]
        
        if condition_list:
            cond_df = pd.DataFrame(condition_list).round(4)
            
            # Save to the 'conditions' subdirectory
            individual_out_path = os.path.join(CONDITION_DIR, files['out'])
            cond_df.to_csv(individual_out_path, index=False)
            
            all_metrics.append(cond_df)

    if all_metrics:
        combined = pd.concat(all_metrics, ignore_index=True)
        combined['condition'] = pd.Categorical(combined['condition'], categories=CONDITION_ORDER, ordered=True)
        
        # Save combined file to the main output directory
        combined_path = os.path.join(OUTPUT_DIR, COMBINED_FILENAME)
        combined.to_csv(combined_path, index=False)
        print(f"\nSuccess!")
        print(f"Individual files: {CONDITION_DIR}")
        print(f"Combined file: {combined_path}")

if __name__ == "__main__":
    main()