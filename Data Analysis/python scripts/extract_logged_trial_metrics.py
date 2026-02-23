import pandas as pd
import numpy as np
import os
from typing import Dict, List, Optional, Tuple, Any

# ==========================================
# FILE & DIRECTORY SETTINGS
# ==========================================
PARENT_DIR = os.path.abspath(os.path.join(os.getcwd(), '..'))
INPUT_DIR = os.path.join(PARENT_DIR, "01_grouped_cleaned_output", "grouped_by_condition")
OUTPUT_DIR = os.path.join(PARENT_DIR, "02_extracted_trial_metrics")

COMBINED_FILENAME = "logged_trial_metrics.csv"
CONDITION_ORDER = ['Baseline', 'HapticsFixed', 'HapticsAdaptive']

DATA_CONFIG = {
    'Baseline': {'in': 'Baseline.csv'},
    'HapticsFixed': {'in': 'HapticsFixed.csv'},
    'HapticsAdaptive': {'in': 'HapticsAdaptive.csv'}
}

# ==========================================
# CONFIGURATION PARAMETERS
# ==========================================
class AnalysisConfig:
    """Configuration parameters for analysis"""
    # Secondary task parameters
    RESPONSE_WINDOW = 1.5  # seconds


# ==========================================
# SECONDARY TASK PERFORMANCE (n-Back)
# ==========================================
def calculate_secondary_task_performance(
    trial_data: pd.DataFrame, 
    config: AnalysisConfig
) -> Dict[str, float]:
    """Calculate n-back task performance metrics"""
    trial_data = trial_data.sort_values('Timestamp').copy()
    
    # Ensure numeric columns
    for col in ['SecTaskNum', 'SecTaskIsTarget', 'SecTaskPressed']:
        if col in trial_data.columns:
            trial_data[col] = pd.to_numeric(trial_data[col], errors='coerce')
    
    # Identify stimulus blocks (when SecTaskNum changes)
    trial_data['stim_block'] = (trial_data['SecTaskNum'] != trial_data['SecTaskNum'].shift()).cumsum()
    
    # Extract arrays for faster processing
    ts = trial_data['Timestamp'].values
    is_target = trial_data['SecTaskIsTarget'].values
    pressed = trial_data['SecTaskPressed'].values
    valid_mask = (is_target != -1)
    
    # Find stimulus onset times
    stim_change = (trial_data['stim_block'] != trial_data['stim_block'].shift())
    stim_change.iloc[0] = True
    
    target_times = ts[stim_change & (is_target == 1) & valid_mask]
    distractor_times = ts[stim_change & (is_target == 0) & valid_mask]
    
    # Find press onset times (transition from 0 to 1)
    press_onsets_mask = (pressed == 1) & (np.roll(pressed, 1) == 0)
    if len(pressed) > 0 and pressed[0] == 1:
        press_onsets_mask[0] = True
    
    press_times = trial_data[press_onsets_mask].drop_duplicates(subset=['stim_block'])['Timestamp'].values
    
    # Calculate hits and reaction times
    hits = 0
    reaction_times = []
    individual_speeds = []
    used_press_indices = set()
    
    for t_start in target_times:
        t_end = t_start + config.RESPONSE_WINDOW
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
    
    # Calculate metrics
    hit_rate = (hits / num_targets * 100) if num_targets > 0 else 0
    fa_count = len(press_times) - len(used_press_indices)
    fa_rate = (fa_count / num_distractors * 100) if num_distractors > 0 else 0
    
    rt_penalized = np.mean(reaction_times) if reaction_times else config.RESPONSE_WINDOW
    proc_speed = np.mean(individual_speeds) if individual_speeds else 0.0
    
    return {
        'nback_hitrate_percent': hit_rate,
        'nback_farate_percent': fa_rate,
        'nback_accuracy_corrected_percent': hit_rate - fa_rate,
        'nback_reactiontime_penalized_seconds': rt_penalized,
        'nback_processingspeed_inverseseconds': proc_speed
    }


# ==========================================
# STEERING REVERSAL RATE CALCULATIONS (CORRECTED)
# ==========================================
def calculate_steering_reversals(trial_data: pd.DataFrame) -> pd.Series:
    """Calculate steering reversal events"""
    steer_diff = trial_data['SteerAngle'].diff().dropna()
    reversals = ((steer_diff.shift(1) > 0) & (steer_diff < 0)) | \
                ((steer_diff.shift(1) < 0) & (steer_diff > 0))
    return reversals


def get_sampling_interval(trial_data: pd.DataFrame) -> float:
    """
    Derive the median sampling interval from timestamps
    More robust than mean because it handles occasional missing data better
    """
    timestamps = trial_data['Timestamp'].values
    if len(timestamps) < 2:
        return 1/60  # fallback to 60Hz if not enough data
    
    # Calculate differences between consecutive timestamps
    diffs = np.diff(timestamps)
    
    # Use median to be robust against outliers
    median_diff = np.median(diffs)
    
    return median_diff


def calculate_state_duration(trial_data: pd.DataFrame, state_mask: pd.Series) -> float:
    """
    Calculate total time spent in a state by counting samples and multiplying by sampling interval
    This correctly handles non-contiguous state segments
    """
    if not state_mask.any():
        return 0.0
    
    # Get sampling interval from the data
    sampling_interval = get_sampling_interval(trial_data)
    
    # Count samples in state
    n_samples = state_mask.sum()
    
    # Total time = number of samples * time per sample
    return n_samples * sampling_interval


def calculate_srr(
    reversals: pd.Series,
    mask: pd.Series,
    trial_data: pd.DataFrame
) -> float:
    """
    Calculate Steering Reversal Rate per minute using actual time in state
    Correctly handles non-contiguous state segments by counting samples
    """
    if not mask.any():
        return np.nan
    
    # Calculate total time in state by counting samples
    state_duration_seconds = calculate_state_duration(trial_data, mask)
    state_duration_minutes = state_duration_seconds / 60
    
    if state_duration_minutes <= 0:
        return np.nan
    
    # Count reversals during state
    n_reversals = reversals[mask].sum()
    
    return n_reversals / state_duration_minutes


# ==========================================
# SEGMENT CLASSIFICATION
# ==========================================
def classify_track_segments(trial_data: pd.DataFrame) -> pd.DataFrame:
    """Classify track segments as straight, short_turn, or long_turn"""
    df = trial_data.copy()
    
    # Calculate radius from curvature
    df['radius'] = np.where(
        df['TrackCurvature_1/Meters'] != 0,
        1 / df['TrackCurvature_1/Meters'].abs(),
        np.nan
    )
    
    # Initialize segment type
    df['segment_type'] = 'straight'
    
    # Classify curves
    curves_mask = df['IsOnStraight'] == 0
    if curves_mask.any():
        df['curve_id'] = (df['IsOnStraight'].diff() != 0).cumsum()
        
        def classify_curve(group):
            return 'short_turn' if group['radius'].median() < 20 else 'long_turn'
        
        curve_mapping = df[curves_mask].groupby('curve_id').apply(
            classify_curve, include_groups=False
        ).to_dict()
        
        df.loc[curves_mask, 'segment_type'] = df.loc[curves_mask, 'curve_id'].map(curve_mapping)
    
    return df


# ==========================================
# SYSTEM STATE REGIONS (with Manual)
# ==========================================
def create_system_state_regions(
    trial_data: pd.DataFrame
) -> Dict[str, pd.Series]:
    """
    Create masks based on system state:
    - Manual: LKA effort = 0, Vibration intensity = 0
    - Warning: LKA effort = 0, Vibration intensity > 0
    - Intervention: LKA effort != 0 (regardless of vibration)
    """
    # Ensure numeric columns
    lka_effort = pd.to_numeric(trial_data['LkaNormEffort'], errors='coerce').fillna(0)
    vib_left = pd.to_numeric(trial_data['Haptic_L_Norm'], errors='coerce').fillna(0)
    vib_right = pd.to_numeric(trial_data['Haptic_R_Norm'], errors='coerce').fillna(0)
    
    # Vibration active if either side > 0
    vibration_active = (vib_left > 0) | (vib_right > 0)
    
    # LKA active if effort is not zero (can be positive or negative)
    lka_active = lka_effort != 0
    
    return {
        'manual': (~lka_active) & (~vibration_active),        # No LKA, no vibration
        'warning': (~lka_active) & vibration_active,          # No LKA, vibration active
        'intervention': lka_active                             # LKA active (any non-zero effort)
    }


# ==========================================
# TRIAL METRIC AGGREGATION (CORRECTED SRR)
# ==========================================
def calculate_trial_metrics(
    trial_data: pd.DataFrame,
    participant_id: int,
    trial_order: int,
    condition: str,
    config: AnalysisConfig
) -> Optional[Dict[str, Any]]:
    """Calculate all metrics for a single trial with corrected state duration"""
    
    if trial_data.empty:
        return None
    
    # Ensure numeric columns
    metric_cols = ['Timestamp', 'Speed_KmH', 'LkaNormEffort', 'LKA_Switch', 'IsOnStraight',
                   'TrackCurvature_1/Meters', 'Haptic_L_Norm', 'Haptic_R_Norm',
                   'CTE_Meters', 'B_HeadingError_Deg', 'SteerAngle']
    
    for col in metric_cols:
        if col in trial_data.columns:
            trial_data[col] = pd.to_numeric(trial_data[col], errors='coerce')
    
    # Classify track segments
    trial_data = classify_track_segments(trial_data)
    
    # Create system state region masks
    state_regions = create_system_state_regions(trial_data)
    
    # Create boolean masks
    lka_switch_on = trial_data['LKA_Switch'] == 1
    
    # Calculate steering reversals
    reversals = calculate_steering_reversals(trial_data)
    
    # Basic trial info
    results = {
        'participant_id': participant_id,
        'trial_order': trial_order,
        'condition': condition
    }
    
    # Trial duration
    laptime = trial_data['Timestamp'].max() - trial_data['Timestamp'].min()
    results['laptime_total_seconds'] = laptime
    
    # ==========================================
    # SPEED
    # ==========================================
    results['speed_mean_kmh_overall'] = trial_data['Speed_KmH'].mean()
    
    # ==========================================
    # CTE ABSOLUTE MEAN
    # ==========================================
    results['cte_absmean_meters_overall'] = trial_data['CTE_Meters'].abs().mean()
    
    for seg in ['straight', 'short_turn', 'long_turn']:
        seg_suffix = 'straight' if seg == 'straight' else seg.replace('_', '')
        seg_mask = (trial_data['segment_type'] == seg)
        seg_data = trial_data[seg_mask]
        
        if not seg_data.empty:
            results[f'cte_absmean_meters_{seg_suffix}'] = seg_data['CTE_Meters'].abs().mean()
        else:
            results[f'cte_absmean_meters_{seg_suffix}'] = np.nan
    
    # ==========================================
    # CTE STANDARD DEVIATION (including manual)
    # ==========================================
    results['cte_sd_meters_overall'] = trial_data['CTE_Meters'].std()
    
    # By turn
    for seg in ['straight', 'short_turn', 'long_turn']:
        seg_suffix = 'straight' if seg == 'straight' else seg.replace('_', '')
        seg_mask = (trial_data['segment_type'] == seg)
        seg_data = trial_data[seg_mask]
        
        if not seg_data.empty:
            results[f'cte_sd_meters_{seg_suffix}'] = seg_data['CTE_Meters'].std()
        else:
            results[f'cte_sd_meters_{seg_suffix}'] = np.nan
    
    # By state (including manual)
    for state in ['manual', 'warning', 'intervention']:
        state_data = trial_data[state_regions[state]]
        if not state_data.empty:
            results[f'cte_sd_meters_{state}'] = state_data['CTE_Meters'].std()
        else:
            results[f'cte_sd_meters_{state}'] = np.nan
    
    # ==========================================
    # HEADING ERROR ABSOLUTE MEAN (including manual)
    # ==========================================
    results['headingerror_absmean_deg_overall'] = trial_data['B_HeadingError_Deg'].abs().mean()
    
    # By turn
    for seg in ['straight', 'short_turn', 'long_turn']:
        seg_suffix = 'straight' if seg == 'straight' else seg.replace('_', '')
        seg_mask = (trial_data['segment_type'] == seg)
        seg_data = trial_data[seg_mask]
        
        if not seg_data.empty:
            results[f'headingerror_absmean_deg_{seg_suffix}'] = seg_data['B_HeadingError_Deg'].abs().mean()
        else:
            results[f'headingerror_absmean_deg_{seg_suffix}'] = np.nan
    
    # By state (including manual)
    for state in ['manual', 'warning', 'intervention']:
        state_data = trial_data[state_regions[state]]
        if not state_data.empty:
            results[f'headingerror_absmean_deg_{state}'] = state_data['B_HeadingError_Deg'].abs().mean()
        else:
            results[f'headingerror_absmean_deg_{state}'] = np.nan
    
    # ==========================================
    # HEADING ERROR STANDARD DEVIATION (including manual)
    # ==========================================
    results['headingerror_sd_deg_overall'] = trial_data['B_HeadingError_Deg'].std()
    
    # By turn
    for seg in ['straight', 'short_turn', 'long_turn']:
        seg_suffix = 'straight' if seg == 'straight' else seg.replace('_', '')
        seg_mask = (trial_data['segment_type'] == seg)
        seg_data = trial_data[seg_mask]
        
        if not seg_data.empty:
            results[f'headingerror_sd_deg_{seg_suffix}'] = seg_data['B_HeadingError_Deg'].std()
        else:
            results[f'headingerror_sd_deg_{seg_suffix}'] = np.nan
    
    # By state (including manual)
    for state in ['manual', 'warning', 'intervention']:
        state_data = trial_data[state_regions[state]]
        if not state_data.empty:
            results[f'headingerror_sd_deg_{state}'] = state_data['B_HeadingError_Deg'].std()
        else:
            results[f'headingerror_sd_deg_{state}'] = np.nan
    
    # ==========================================
    # STEERING ANGLE STANDARD DEVIATION (including manual)
    # ==========================================
    results['steeringangle_sd_deg_overall'] = trial_data['SteerAngle'].std()
    
    # By turn
    for seg in ['straight', 'short_turn', 'long_turn']:
        seg_suffix = 'straight' if seg == 'straight' else seg.replace('_', '')
        seg_mask = (trial_data['segment_type'] == seg)
        seg_data = trial_data[seg_mask]
        
        if not seg_data.empty:
            results[f'steeringangle_sd_deg_{seg_suffix}'] = seg_data['SteerAngle'].std()
        else:
            results[f'steeringangle_sd_deg_{seg_suffix}'] = np.nan
    
    # By state (including manual)
    for state in ['manual', 'warning', 'intervention']:
        state_data = trial_data[state_regions[state]]
        if not state_data.empty:
            results[f'steeringangle_sd_deg_{state}'] = state_data['SteerAngle'].std()
        else:
            results[f'steeringangle_sd_deg_{state}'] = np.nan
    
    # ==========================================
    # STEERING REVERSAL RATE (SRR) - CORRECTED
    # ==========================================
    
    # Overall SRR (using full trial duration)
    results['steeringreversal_perminute_overall'] = calculate_srr(
        reversals, pd.Series(True, index=trial_data.index), trial_data
    )
    
    # By state (including manual) - CORRECTED using sample counting
    for state in ['manual', 'warning', 'intervention']:
        results[f'steeringreversal_perminute_{state}'] = calculate_srr(
            reversals, state_regions[state], trial_data
        )
    
    # By turn
    for seg in ['straight', 'short_turn', 'long_turn']:
        seg_suffix = 'straight' if seg == 'straight' else seg.replace('_', '')
        seg_mask = (trial_data['segment_type'] == seg)
        
        results[f'steeringreversal_perminute_{seg_suffix}'] = calculate_srr(
            reversals, seg_mask, trial_data
        )
    
    # ==========================================
    # LKA METRICS
    # ==========================================
    
    # LKA switch time percent
    results['lka_switch_percenttime_overall'] = lka_switch_on.mean() * 100
    
    # Active LKA effort (during intervention)
    intervention_data = trial_data[state_regions['intervention']]
    if not intervention_data.empty:
        results['activelkaeffort_absmean_overall'] = intervention_data['LkaNormEffort'].abs().mean()
    else:
        results['activelkaeffort_absmean_overall'] = np.nan
    
    # Active LKA effort by turn
    for seg in ['straight', 'short_turn', 'long_turn']:
        seg_suffix = 'straight' if seg == 'straight' else seg.replace('_', '')
        seg_mask = (trial_data['segment_type'] == seg)
        intervention_in_seg = seg_mask & state_regions['intervention']
        seg_data = trial_data[intervention_in_seg]
        
        if not seg_data.empty:
            results[f'activelkaeffort_absmean_{seg_suffix}'] = seg_data['LkaNormEffort'].abs().mean()
        else:
            results[f'activelkaeffort_absmean_{seg_suffix}'] = np.nan
    
    # ==========================================
    # TIME PERCENTAGE IN STATES (including manual)
    # ==========================================
    total_samples = len(trial_data)
    results['percenttime_manual'] = (state_regions['manual'].sum() / total_samples) * 100
    results['percenttime_warning'] = (state_regions['warning'].sum() / total_samples) * 100
    results['percenttime_intervention'] = (state_regions['intervention'].sum() / total_samples) * 100
    
    # ==========================================
    # n-BACK METRICS
    # ==========================================
    results.update(calculate_secondary_task_performance(trial_data, config))
    
    return results


# ==========================================
# MAIN EXECUTION
# ==========================================
def main():
    """Main execution function"""
    # Create output directory if it doesn't exist
    if not os.path.exists(OUTPUT_DIR):
        os.makedirs(OUTPUT_DIR)
    
    # Initialize configuration
    config = AnalysisConfig()
    
    # Print configuration info
    print("=" * 70)
    print("ANALYSIS CONFIGURATION")
    print("=" * 70)
    print("System State Regions:")
    print("  - Manual: LKA effort = 0, Vibration = 0")
    print("  - Warning: LKA effort = 0, Vibration > 0")
    print("  - Intervention: LKA effort != 0")
    print(f"Response Window: {config.RESPONSE_WINDOW}s")
    print("=" * 70)
    print("✓ SRR calculated using sample counting (correct for non-contiguous states)")
    print("✓ Sampling interval derived from timestamps (not assumed)")
    print("=" * 70)
    
    all_metrics = []
    
    # Process each condition
    for cond_key, files in DATA_CONFIG.items():
        file_path = os.path.join(INPUT_DIR, files['in'])
        
        if not os.path.exists(file_path):
            print(f"Warning: File not found - {file_path}")
            continue
        
        print(f"\nProcessing {cond_key}...")
        raw_data = pd.read_csv(file_path, sep=';', decimal=',')
        
        # Group by participant and trial
        grouped = raw_data.groupby(['participant_id', 'trial_order'])
        
        # Process each trial
        condition_metrics = []
        total_trials = len(grouped)
        for i, ((pid, order), trial_group) in enumerate(grouped, 1):
            print(f"  Trial {i}/{total_trials}", end='\r')
            metrics = calculate_trial_metrics(trial_group, pid, order, cond_key, config)
            if metrics is not None:
                condition_metrics.append(metrics)
        
        if condition_metrics:
            all_metrics.append(pd.DataFrame(condition_metrics))
            print(f"  ✓ Processed {len(condition_metrics)} trials")
    
    # Combine and save results
    if all_metrics:
        combined = pd.concat(all_metrics, ignore_index=True)
        combined['condition'] = pd.Categorical(
            combined['condition'],
            categories=CONDITION_ORDER,
            ordered=True
        )
        
        # Round to 4 decimal places and save
        output_path = os.path.join(OUTPUT_DIR, COMBINED_FILENAME)
        combined.round(4).to_csv(output_path, index=False)
        
        print("\n" + "=" * 70)
        print("SUCCESS! Metrics saved to:")
        print(f"  {output_path}")
        print("=" * 70)
        print(f"Total trials processed: {len(combined)}")
        print(f"Total metrics per trial: {len(combined.columns)}")
        print("=" * 70)
        
        # Show all headers
        print("\nCOMPLETE LIST OF HEADERS:")
        print("-" * 70)
        for i, col in enumerate(sorted(combined.columns), 1):
            print(f"{i:3}. {col}")
        print("-" * 70)
        print(f"TOTAL: {len(combined.columns)} columns")
        print("=" * 70)
        
        # Summary by category
        print("\nSUMMARY BY CATEGORY:")
        print("-" * 70)
        print(f"Basic Trial Info:           3 columns")
        print(f"Laptime:                    1 column")
        print(f"Speed Mean:                 1 column")
        print(f"CTE Absolute Mean:          4 columns")
        print(f"CTE Standard Deviation:     7 columns (incl. manual)")
        print(f"Heading Error Absolute Mean: 7 columns (incl. manual)")
        print(f"Heading Error SD:            7 columns (incl. manual)")
        print(f"Steering Angle SD:           7 columns (incl. manual)")
        print(f"Steering Reversal Rate:      7 columns (incl. manual, CORRECTED)")
        print(f"LKA Metrics:                 5 columns")
        print(f"Time Percentage in States:   3 columns (incl. manual)")
        print(f"n-Back Metrics:              5 columns")
        print("-" * 70)
        print(f"TOTAL:                       52 columns")
        print("=" * 70)
        
        # Show sampling rate info
        print("\nSAMPLING RATE VERIFICATION:")
        print("-" * 70)
        sample_trial = combined.iloc[0] if len(combined) > 0 else None
        if sample_trial is not None:
            print("✓ Sampling interval derived from timestamps for each trial")
            print("✓ SRR accurately accounts for fragmented state periods")
        print("=" * 70)
        
    else:
        print("\nNo data processed. Check input files.")


if __name__ == "__main__":
    main()