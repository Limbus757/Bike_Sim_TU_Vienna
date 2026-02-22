import pandas as pd
import numpy as np
import os

# ==========================================
# FILE & DIRECTORY SETTINGS
# ==========================================
PARENT_DIR = os.path.abspath(os.path.join(os.getcwd(), '..'))
INPUT_DIR  = os.path.join(PARENT_DIR, "01_grouped_cleaned_output", "grouped_by_condition")
OUTPUT_DIR = os.path.join(PARENT_DIR, "02_extracted_trial_metrics")

COMBINED_FILENAME = "logged_trial_metrics.csv"
CONDITION_ORDER = ['Baseline', 'HapticsFixed', 'HapticsAdaptive']

DATA_CONFIG = {
    'Baseline': {'in': 'Baseline.csv'},
    'HapticsFixed': {'in': 'HapticsFixed.csv'},
    'HapticsAdaptive': {'in': 'HapticsAdaptive.csv'}
}

# ==========================================
# SECONDARY TASK PERFORMANCE (n-Back)
# ==========================================

def calculate_secondary_task_performance(trial_data, response_window=1.5):
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
    hits, reaction_times, individual_speeds, used_press_indices = 0, [], [], set()
    
    for t_start in target_times:
        t_end = t_start + response_window
        for i, p_time in enumerate(press_times):
            if i not in used_press_indices and t_start <= p_time <= t_end:
                hits += 1
                rt = p_time - t_start
                reaction_times.append(rt)
                individual_speeds.append(1.0 / rt)
                used_press_indices.add(i)
                break

    num_targets = len(target_times)
    num_distractors = len(distractor_times)
    
    # hit rate as percentage of targets identified
    hit_rate = (hits / num_targets * 100) if num_targets > 0 else 0
    fa_count = len(press_times) - len(used_press_indices)
    # false alarm rate based on distractor responses
    fa_rate = (fa_count / num_distractors * 100) if num_distractors > 0 else 0
    # corrected accuracy subtracting false alarms from hits
    # reaction time average with penalty for misses
    rt_penalized = np.mean(reaction_times) if reaction_times else response_window
    # processing speed as average of inverse reaction times
    proc_speed = np.mean(individual_speeds) if individual_speeds else 0.0

    return {
        'nback_hitrate_pct': hit_rate,
        'nback_farate_pct': fa_rate,
        'nback_accuracy_corrected_pct': hit_rate - fa_rate,
        'nback_penalized_rt_seconds': rt_penalized,
        'nback_processing_speed': proc_speed
    }

# ==========================================
# TRIAL METRIC AGGREGATION
# ==========================================

def calculate_trial_metrics(trial_data, participant_id, trial_order, condition):
    if trial_data.empty: return None
    
    metric_cols = ['Timestamp', 'Speed_KmH', 'LkaNormEffort', 'LKA_Switch', 'IsOnStraight',
                   'TrackCurvature_1/Meters', 'Haptic_L_Norm', 'Haptic_R_Norm', 
                   'CTE_Meters', 'B_HeadingError_Deg', 'SteerAngle']
    
    for col in metric_cols:
        if col in trial_data.columns:
            trial_data[col] = pd.to_numeric(trial_data[col], errors='coerce')

    # active torque detection
    is_lka_active = trial_data['LkaNormEffort'].abs() > 0
    # vibration activity detection
    is_vibrating = (trial_data['Haptic_L_Norm'] > 0) | (trial_data['Haptic_R_Norm'] > 0)
    is_switched_on = (trial_data['LKA_Switch'] == 1)
    # edge region occupancy detection
    is_in_edge = trial_data['CTE_Meters'].abs() > 0.5

    results = {'participant_id': participant_id, 'trial_order': trial_order, 'condition': condition}

    # turn radius and segment classification
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

    # total duration of the trial
    laptime = trial_data['Timestamp'].max() - trial_data['Timestamp'].min()
    results['laptime_total_seconds'] = laptime
    
    # overall steering reversal count per minute
    steer_diff = trial_data['SteerAngle'].diff().dropna()
    reversals = ((steer_diff.shift(1) > 0) & (steer_diff < 0)) | ((steer_diff.shift(1) < 0) & (steer_diff > 0))
    results['steering_reversal_rate'] = reversals.sum() / (laptime / 60) if laptime > 0 else 0
    
    # active duration of haptic vibrations
    vibe_time = is_vibrating.sum() * (1/60)
    # active duration of steering motor torque
    lka_time = is_lka_active.sum() * (1/60)
    
    # reversal rate specifically during vibration warnings
    results['srr_during_vibration'] = reversals[is_vibrating].sum() / vibe_time if vibe_time > 0 else 0
    # reversal rate specifically during active lka torque
    results['srr_during_lka'] = reversals[is_lka_active].sum() / lka_time if lka_time > 0 else 0

    # absolute mean cross track error for overall trial
    results['cte_absmean_meters'] = trial_data['CTE_Meters'].abs().mean()
    # percentage of time spent outside lane center safety margin
    results['pct_time_edge_region'] = is_in_edge.mean() * 100

    for seg in ['straight', 'short_turn', 'long_turn']:
        seg_mask = (trial_data['segment_type'] == seg)
        seg_data = trial_data[seg_mask]
        
        if not seg_data.empty:
            seg_reversals = reversals[seg_mask]
            seg_vibe = is_vibrating[seg_mask]
            seg_lka = is_lka_active[seg_mask]
            
            s_time = (seg_data['Timestamp'].max() - seg_data['Timestamp'].min()) / 60
            sv_time = seg_vibe.sum() * (1/60)
            sl_time = seg_lka.sum() * (1/60)

            # segmented reversal rate for current track type
            results[f'srr_{seg}'] = seg_reversals.sum() / s_time if s_time > 0 else 0
            # segmented reversal rate during vibration on specific segment
            results[f'srr_vibration_{seg}'] = seg_reversals[seg_vibe].sum() / sv_time if sv_time > 0 else 0
            # segmented reversal rate during lka torque on specific segment
            results[f'srr_lka_{seg}'] = seg_reversals[seg_lka].sum() / sl_time if sl_time > 0 else 0
            # absolute cross track error for current segment
            results[f'cte_absmean_{seg}'] = seg_data['CTE_Meters'].abs().mean()
        else:
            results[f'srr_{seg}'] = np.nan
            results[f'srr_vibration_{seg}'] = np.nan
            results[f'srr_lka_{seg}'] = np.nan

    results.update(calculate_secondary_task_performance(trial_data))
    return results

# ==========================================
# MAIN EXECUTION
# ==========================================

def main():
    if not os.path.exists(OUTPUT_DIR): os.makedirs(OUTPUT_DIR)
    all_metrics = []
    
    for cond_key, files in DATA_CONFIG.items():
        file_path = os.path.join(INPUT_DIR, files['in'])
        if not os.path.exists(file_path): continue
            
        print(f"Processing {cond_key}...")
        raw_data = pd.read_csv(file_path, sep=';', decimal=',')
        
        grouped = raw_data.groupby(['participant_id', 'trial_order'])
        condition_list = [calculate_trial_metrics(tg, pid, order, cond_key) for (pid, order), tg in grouped]
        condition_list = [m for m in condition_list if m is not None]
        
        if condition_list:
            all_metrics.append(pd.DataFrame(condition_list))

    if all_metrics:
        combined = pd.concat(all_metrics, ignore_index=True)
        combined['condition'] = pd.Categorical(combined['condition'], categories=CONDITION_ORDER, ordered=True)
        combined.round(4).to_csv(os.path.join(OUTPUT_DIR, COMBINED_FILENAME), index=False)
        print(f"Success! Metrics saved to {OUTPUT_DIR}")

if __name__ == "__main__":
    main()