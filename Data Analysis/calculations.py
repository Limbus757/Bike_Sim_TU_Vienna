"""
TRIAL-LEVEL METRIC CALCULATIONS
Calculates all required metrics for each trial
"""
import pandas as pd
import numpy as np

# ============================================================================
# BASIC STATISTICAL FUNCTIONS
# ============================================================================

def calculate_basic_stats(values):
    """Calculate mean, median, std, and CV for a set of values."""
    if len(values) == 0:
        return {'mean': np.nan, 'median': np.nan, 'std': np.nan, 'cv': np.nan}
    
    values = np.array(values)
    mean_val = np.mean(values)
    median_val = np.median(values)
    std_val = np.std(values)
    
    # Coefficient of Variation (avoid division by zero)
    cv_val = std_val / mean_val if mean_val != 0 else np.nan
    
    return {
        'mean': float(mean_val),
        'median': float(median_val),
        'std': float(std_val),
        'cv': float(cv_val)
    }

# ============================================================================
# DRIVING METRIC CALCULATIONS
# ============================================================================

def calculate_speed_metrics(trial_data):
    """Calculate speed metrics for a trial."""
    speed_values = trial_data['Speed_KmH'].dropna().values
    
    return {
        'Speed_Mean': np.mean(speed_values) if len(speed_values) > 0 else np.nan,
        'Speed_Median': np.median(speed_values) if len(speed_values) > 0 else np.nan,
        'Speed_SD': np.std(speed_values) if len(speed_values) > 0 else np.nan,
        'Speed_CV': (np.std(speed_values) / np.mean(speed_values)) 
                    if len(speed_values) > 0 and np.mean(speed_values) != 0 else np.nan
    }

def calculate_cte_metrics(trial_data):
    """Calculate CTE (Cross-Track Error) metrics for a trial."""
    cte_values = trial_data['CTE_Meters'].dropna().values
    
    if len(cte_values) == 0:
        return {}
    
    # Absolute CTE metrics
    abs_cte_values = np.abs(cte_values)
    
    cte_metrics = {
        # Non-absolute CTE
        'CTE_NonAbs_Mean': float(np.mean(cte_values)),
        'CTE_NonAbs_Median': float(np.median(cte_values)),
        'CTE_NonAbs_SD': float(np.std(cte_values)),
        'CTE_NonAbs_CV': float(np.std(cte_values) / np.mean(cte_values)) if np.mean(cte_values) != 0 else np.nan,
        
        # Absolute CTE
        'CTE_Abs_Mean': float(np.mean(abs_cte_values)),
        'CTE_Abs_Median': float(np.median(abs_cte_values)),
        'CTE_Abs_SD': float(np.std(abs_cte_values)),
        'CTE_Abs_CV': float(np.std(abs_cte_values) / np.mean(abs_cte_values)) if np.mean(abs_cte_values) != 0 else np.nan
    }
    
    return cte_metrics

def calculate_heading_metrics(trial_data):
    """Calculate heading error metrics for a trial."""
    heading_values = trial_data['B_HeadingError_Deg'].dropna().values
    
    if len(heading_values) == 0:
        return {}
    
    # Absolute heading error metrics
    abs_heading_values = np.abs(heading_values)
    
    heading_metrics = {
        # Non-absolute heading error
        'Heading_NonAbs_Mean': float(np.mean(heading_values)),
        'Heading_NonAbs_Median': float(np.median(heading_values)),
        'Heading_NonAbs_SD': float(np.std(heading_values)),
        'Heading_NonAbs_CV': float(np.std(heading_values) / np.mean(heading_values)) if np.mean(heading_values) != 0 else np.nan,
        
        # Absolute heading error
        'Heading_Abs_Mean': float(np.mean(abs_heading_values)),
        'Heading_Abs_Median': float(np.median(abs_heading_values)),
        'Heading_Abs_SD': float(np.std(abs_heading_values)),
        'Heading_Abs_CV': float(np.std(abs_heading_values) / np.mean(abs_heading_values)) if np.mean(abs_heading_values) != 0 else np.nan,
        
        # Heading variability (SD of heading error)
        'Heading_Variability': float(np.std(heading_values))
    }
    
    return heading_metrics

def calculate_sdlp(trial_data):
    """Calculate Standard Deviation of Lane Position (SDLP)."""
    cte_values = trial_data['CTE_Meters'].dropna().values
    
    if len(cte_values) > 1:
        sdlp = np.std(cte_values)
        return {'SDLP': float(sdlp)}
    else:
        return {'SDLP': np.nan}

# ============================================================================
# SECONDARY TASK CALCULATIONS
# ============================================================================

def calculate_secondary_task_performance(trial_data, response_window=1.5):
    """
    Calculate secondary task performance metrics for a trial.
    
    Args:
        trial_data: DataFrame with timestamped trial data
        response_window: Time window in seconds for valid responses
    
    Returns: Hit rate, false positive rate, and corrected accuracy
    """
    # Sort by timestamp
    trial_data = trial_data.sort_values('Timestamp').reset_index(drop=True)
    
    # Get arrays for faster processing
    timestamps = trial_data['Timestamp'].values
    target_flags = trial_data['SecTaskIsTarget'].values
    press_flags = trial_data['SecTaskPressed'].values
    
    # Find stimulus changes (target or distractor appearances)
    stimulus_changes = []
    for i in range(len(trial_data)):
        if target_flags[i] != -1:  # Valid stimulus
            if i == 0 or target_flags[i] != target_flags[i-1]:
                stimulus_changes.append((timestamps[i], target_flags[i]))
    
    if not stimulus_changes:
        return {
            'SecTask_HitRate': 0,
            'SecTask_FARate': 0,
            'SecTask_CorrectedAcc': 0
        }
    
    # Separate targets and distractors
    target_times = [t for t, flag in stimulus_changes if flag == 1]
    distractor_count = sum(1 for _, flag in stimulus_changes if flag == 0)
    
    # Find button press events (transitions from 0 to 1)
    press_times = []
    for i in range(1, len(trial_data)):
        if press_flags[i] == 1 and press_flags[i-1] == 0:
            press_times.append(timestamps[i])
    
    # Count hits within response window
    hits = 0
    used_presses = set()
    
    for target_time in target_times:
        window_end = target_time + response_window
        for press_idx, press_time in enumerate(press_times):
            if press_idx not in used_presses and target_time <= press_time <= window_end:
                used_presses.add(press_idx)
                hits += 1
                break
    
    # Calculate rates
    hit_rate = (hits / len(target_times)) * 100 if target_times else 0
    false_alarms = len(press_times) - len(used_presses)
    false_alarm_rate = (false_alarms / distractor_count) * 100 if distractor_count > 0 else 0
    corrected_accuracy = hit_rate - false_alarm_rate
    
    return {
        'SecTask_HitRate': hit_rate,
        'SecTask_FARate': false_alarm_rate,
        'SecTask_CorrectedAcc': corrected_accuracy
    }

# ============================================================================
# LKA SYSTEM METRICS
# ============================================================================

def calculate_lka_switch_metrics(trial_data):
    """Calculate LKA switch engagement metrics for a trial."""
    lka_switch_values = trial_data['LKA_Switch'].dropna().values
    
    if len(lka_switch_values) == 0:
        return {'LKA_Switch_Pct': np.nan}
    
    # Percentage of time LKA switch was ON (value = 1)
    switch_on_pct = (np.sum(lka_switch_values == 1) / len(lka_switch_values)) * 100
    
    return {'LKA_Switch_Pct': float(switch_on_pct)}

def calculate_lka_engagement_metrics(trial_data):
    """Calculate LKA engagement metrics when switch was ON."""
    # Filter for when LKA switch was ON
    lka_on_data = trial_data[trial_data['LKA_Switch'] == 1]
    
    if len(lka_on_data) == 0:
        return {
            'LKA_Engaged_Pct': np.nan,
            'LkaEffort_Mean': np.nan,
            'LkaEffort_Median': np.nan,
            'LkaEffort_SD': np.nan,
            'LkaEffort_CV': np.nan
        }
    
    # LKA engaged percentage (when LKA_Engaged = 1)
    lka_engaged_values = lka_on_data['LKA_Engaged'].dropna().values
    if len(lka_engaged_values) > 0:
        engaged_pct = (np.sum(lka_engaged_values == 1) / len(lka_engaged_values)) * 100
    else:
        engaged_pct = np.nan
    
    # LKA effort metrics (absolute values)
    lka_effort_values = lka_on_data['LkaNormEffort'].dropna().values
    if len(lka_effort_values) > 0:
        abs_effort_values = np.abs(lka_effort_values)
        
        effort_mean = np.mean(abs_effort_values)
        effort_median = np.median(abs_effort_values)
        effort_std = np.std(abs_effort_values)
        effort_cv = effort_std / effort_mean if effort_mean != 0 else np.nan
    else:
        effort_mean = effort_median = effort_std = effort_cv = np.nan
    
    return {
        'LKA_Engaged_Pct': float(engaged_pct),
        'LkaEffort_Mean': float(effort_mean),
        'LkaEffort_Median': float(effort_median),
        'LkaEffort_SD': float(effort_std),
        'LkaEffort_CV': float(effort_cv)
    }

# ============================================================================
# VIBRATION METRICS
# ============================================================================

def calculate_vibration_metrics(trial_data):
    """
    Calculate normalized vibration metrics for left and right.
    Assuming columns: 'Vibration_Left_Norm' and 'Vibration_Right_Norm'
    """
    vibration_metrics = {}
    
    # Check if vibration columns exist
    left_vib_col = 'Vibration_Left_Norm'
    right_vib_col = 'Vibration_Right_Norm'
    
    if left_vib_col in trial_data.columns:
        left_values = trial_data[left_vib_col].dropna().values
        if len(left_values) > 0:
            vib_stats = calculate_basic_stats(left_values)
            vibration_metrics.update({
                'Vib_Left_Mean': vib_stats['mean'],
                'Vib_Left_Median': vib_stats['median'],
                'Vib_Left_SD': vib_stats['std'],
                'Vib_Left_CV': vib_stats['cv']
            })
    
    if right_vib_col in trial_data.columns:
        right_values = trial_data[right_vib_col].dropna().values
        if len(right_values) > 0:
            vib_stats = calculate_basic_stats(right_values)
            vibration_metrics.update({
                'Vib_Right_Mean': vib_stats['mean'],
                'Vib_Right_Median': vib_stats['median'],
                'Vib_Right_SD': vib_stats['std'],
                'Vib_Right_CV': vib_stats['cv']
            })
    
    return vibration_metrics

# ============================================================================
# MAIN TRIAL PROCESSING FUNCTION
# ============================================================================

def calculate_trial_metrics(trial_data, participant_id, trial_order, condition):
    """
    Calculate all metrics for a single trial.
    
    Args:
        trial_data: DataFrame with raw trial data
        participant_id: Participant identifier
        trial_order: Trial number/order
        condition: Condition name (Baseline, HapticsFixed, HapticsAdaptive)
    
    Returns: Dictionary with all calculated metrics
    """
    if trial_data.empty:
        return None
    
    # Initialize results dictionary
    results = {
        'participant_id': participant_id,
        'trial_order': trial_order,
        'Condition': condition,
        
        # Laptime per trial
        'Laptime': trial_data['Timestamp'].max() - trial_data['Timestamp'].min()
    }
    
    # Calculate all metrics
    metrics_functions = [
        calculate_speed_metrics,
        calculate_cte_metrics,
        calculate_heading_metrics,
        calculate_sdlp,
        calculate_secondary_task_performance,
        calculate_lka_switch_metrics,
        calculate_lka_engagement_metrics,
        calculate_vibration_metrics
    ]
    
    for metric_func in metrics_functions:
        try:
            metric_results = metric_func(trial_data)
            results.update(metric_results)
        except Exception as e:
            print(f"Error calculating {metric_func.__name__} for participant {participant_id}, trial {trial_order}: {e}")
    
    return results

def process_all_trials(raw_data, condition_name):
    """
    Process all trials for a given condition.
    
    Args:
        raw_data: Raw DataFrame with all trials for a condition
        condition_name: Name of the condition
    
    Returns: DataFrame with trial-level metrics
    """
    # Group by participant and trial
    grouped = raw_data.groupby(['participant_id', 'trial_order'])
    
    all_trial_metrics = []
    
    print(f"Processing {len(grouped)} trials for {condition_name}...")
    
    for (participant_id, trial_order), trial_group in grouped:
        # Calculate metrics for this trial
        trial_metrics = calculate_trial_metrics(
            trial_group, 
            participant_id, 
            trial_order, 
            condition_name
        )
        
        if trial_metrics:
            all_trial_metrics.append(trial_metrics)
    
    # Convert to DataFrame
    if all_trial_metrics:
        metrics_df = pd.DataFrame(all_trial_metrics)
        print(f"Successfully processed {len(metrics_df)} trials")
        return metrics_df
    else:
        print(f"No trials processed for {condition_name}")
        return pd.DataFrame()