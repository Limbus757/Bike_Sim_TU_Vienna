"""
Streamlined data processing - minimal output
"""
import pandas as pd
import numpy as np
import os
import time

# Configuration
CONDITION_ORDER = ['Baseline', 'HapticsFixed', 'HapticsAdaptive']
DATA_FILENAMES = {
    'Baseline': 'Baseline.csv',
    'HapticsFixed': 'HapticsFixed.csv', 
    'HapticsAdaptive': 'HapticsAdaptive.csv'
}
DATA_SEARCH_PATHS = ["cleaned_grouped_output/grouped_by_condition"]

def calculate_basic_stats(values):
    if len(values) == 0:
        return {'mean': np.nan, 'median': np.nan, 'std': np.nan, 'cv': np.nan}
    
    values = np.array(values)
    mean_val = np.mean(values)
    median_val = np.median(values)
    std_val = np.std(values)
    cv_val = std_val / mean_val if mean_val != 0 else np.nan
    
    return {
        'mean': float(mean_val),
        'median': float(median_val),
        'std': float(std_val),
        'cv': float(cv_val)
    }

def calculate_secondary_task_performance(trial_data, response_window=1.5):
    trial_data = trial_data.sort_values('Timestamp').reset_index(drop=True)
    
    timestamps = trial_data['Timestamp'].values
    target_flags = trial_data['SecTaskIsTarget'].values
    press_flags = trial_data['SecTaskPressed'].values
    
    stimulus_changes = []
    for i in range(len(trial_data)):
        if target_flags[i] != -1:
            if i == 0 or target_flags[i] != target_flags[i-1]:
                stimulus_changes.append((timestamps[i], target_flags[i]))
    
    if not stimulus_changes:
        return {'HitRate': 0, 'FARate': 0, 'CorrectedAcc': 0}
    
    target_times = [t for t, flag in stimulus_changes if flag == 1]
    distractor_count = sum(1 for _, flag in stimulus_changes if flag == 0)
    
    press_times = []
    for i in range(1, len(trial_data)):
        if press_flags[i] == 1 and press_flags[i-1] == 0:
            press_times.append(timestamps[i])
    
    hits = 0
    used_presses = set()
    
    for target_time in target_times:
        window_end = target_time + response_window
        for press_idx, press_time in enumerate(press_times):
            if press_idx not in used_presses and target_time <= press_time <= window_end:
                used_presses.add(press_idx)
                hits += 1
                break
    
    hit_rate = (hits / len(target_times)) * 100 if target_times else 0
    false_alarms = len(press_times) - len(used_presses)
    false_alarm_rate = (false_alarms / distractor_count) * 100 if distractor_count > 0 else 0
    
    return {
        'HitRate': hit_rate,
        'FARate': false_alarm_rate,
        'CorrectedAcc': hit_rate - false_alarm_rate
    }

def calculate_trial_metrics(trial_data, participant_id, trial_order, condition):
    if trial_data.empty:
        return None
    
    results = {
        'participant_id': participant_id,
        'trial_order': trial_order,
        'Condition': condition
    }
    
    # Calculate laptime
    timestamps = trial_data['Timestamp'].dropna().values
    if len(timestamps) > 0:
        time_min = np.min(timestamps)
        time_max = np.max(timestamps)
        laptime = time_max - time_min
        results['Laptime'] = laptime
    
    # Speed metrics
    speed_values = trial_data['Speed_KmH'].dropna().values
    if len(speed_values) > 0:
        stats = calculate_basic_stats(speed_values)
        results.update({f'Speed_{k}': v for k, v in stats.items()})
    
    # CTE metrics
    cte_values = trial_data['CTE_Meters'].dropna().values
    if len(cte_values) > 0:
        cte_std = np.std(cte_values)
        results['CTE_Std'] = float(cte_std)
        abs_cte = np.abs(cte_values)
        results['CTE_Abs_Mean'] = float(np.mean(abs_cte))
        results['CTE_Abs_Median'] = float(np.median(abs_cte))
    
    # Heading error metrics
    heading_values = trial_data['B_HeadingError_Deg'].dropna().values
    if len(heading_values) > 0:
        heading_std = np.std(heading_values)
        results['Heading_Std'] = float(heading_std)
        abs_heading = np.abs(heading_values)
        results['Heading_Abs_Mean'] = float(np.mean(abs_heading))
        results['Heading_Abs_Median'] = float(np.median(abs_heading))
    
    # Secondary task
    sec_task = calculate_secondary_task_performance(trial_data)
    results.update(sec_task)
    
    # LKA calculations (CRITICAL SECTION)
    if 'LKA_Switch' in trial_data.columns and 'LKA_Engaged' in trial_data.columns:
        # Sort data by timestamp
        trial_data = trial_data.sort_values('Timestamp').reset_index(drop=True)
        
        timestamps = trial_data['Timestamp'].values
        switch_values = trial_data['LKA_Switch'].values
        engaged_values = trial_data['LKA_Engaged'].values
        
        # 1. SWITCH ON PERCENTAGE (time-based, not sample-based)
        if len(timestamps) > 1:
            # Calculate time intervals
            time_intervals = np.diff(timestamps)
            
            # Calculate total time switch was ON
            switch_on_mask = switch_values[:-1] == 1  # For each interval
            switch_on_time = np.sum(time_intervals[switch_on_mask])
            total_time = timestamps[-1] - timestamps[0]
            
            if total_time > 0:
                results['LKA_SwitchOn_Time_Pct'] = (switch_on_time / total_time) * 100
                results['LKA_SwitchOn_Total_Time'] = switch_on_time
        
        # 2. ACTIVATION TIME WHEN SWITCH ON
        # Condition: Switch ON AND Engaged
        switch_on_engaged_mask = (switch_values == 1) & (engaged_values == 1)
        
        if np.any(switch_on_engaged_mask) and len(timestamps) > 1:
            # Find consecutive engaged periods when switch is ON
            engaged_indices = np.where(switch_on_engaged_mask)[0]
            
            if len(engaged_indices) > 1:
                # Calculate durations between engaged samples
                engaged_timestamps = timestamps[engaged_indices]
                durations = np.diff(engaged_timestamps)
                
                # Filter reasonable durations (< 2 seconds between samples)
                reasonable_durations = durations[durations < 2]
                
                if len(reasonable_durations) > 0:
                    total_activation = np.sum(reasonable_durations)
                    results['LKA_SwitchOn_Activation_Total_Time'] = total_activation
                    
                    # Percentage of switch-on time that was actually engaged
                    if 'LKA_SwitchOn_Total_Time' in results and results['LKA_SwitchOn_Total_Time'] > 0:
                        results['LKA_SwitchOn_Activation_Pct'] = (total_activation / results['LKA_SwitchOn_Total_Time']) * 100
                    
                    # Stats for activation durations
                    duration_stats = calculate_basic_stats(reasonable_durations)
                    results.update({
                        f'LKA_SwitchOn_Activation_Duration_{k}': v 
                        for k, v in duration_stats.items()
                    })
    
    # 3. LKA EFFORT - ONLY when switch is ON
    if 'LkaNormEffort' in trial_data.columns and 'LKA_Switch' in trial_data.columns:
        # Filter for when LKA switch is ON
        lka_on_data = trial_data[trial_data['LKA_Switch'] == 1]
        effort = lka_on_data['LkaNormEffort'].dropna().values
        
        if len(effort) > 0:
            abs_effort = np.abs(effort)
            stats = calculate_basic_stats(abs_effort)
            results.update({f'LkaEffort_{k}': v for k, v in stats.items()})
            
            # Also add sample count for context
            results['LkaEffort_Samples'] = len(effort)
        else:
            # Set to NaN if no effort data when switch was ON
            results.update({
                'LkaEffort_mean': np.nan,
                'LkaEffort_median': np.nan,
                'LkaEffort_std': np.nan,
                'LkaEffort_cv': np.nan,
                'LkaEffort_Samples': 0
            })
    
    # Vibration metrics
    vib_cols = {'Left': 'Haptic_L_Norm', 'Right': 'Haptic_R_Norm'}
    for side, col in vib_cols.items():
        if col in trial_data.columns:
            vib = trial_data[col].dropna().values
            if len(vib) > 0:
                stats = calculate_basic_stats(vib)
                results.update({f'Vib_{side}_{k}': v for k, v in stats.items()})
    
    return results

def process_all_trials(raw_data, condition_name):
    grouped = raw_data.groupby(['participant_id', 'trial_order'])
    all_metrics = []
    
    print(f"Processing {len(grouped)} trials")
    
    for (pid, order), trial_group in grouped:
        metrics = calculate_trial_metrics(trial_group, pid, order, condition_name)
        if metrics:
            all_metrics.append(metrics)
    
    if all_metrics:
        df = pd.DataFrame(all_metrics)
        print(f"Processed {len(df)} trials")
        return df
    return pd.DataFrame()

def find_data_file(filename):
    for path in DATA_SEARCH_PATHS:
        full_path = os.path.join(path, filename)
        if os.path.exists(full_path):
            return full_path
    return None

def load_condition_data(condition_name, filename):
    print(f"Loading {condition_name}")
    
    file_path = find_data_file(filename)
    if not file_path:
        print(f"File not found: {filename}")
        return None
    
    try:
        raw_data = pd.read_csv(file_path, sep=';', decimal=',')
        print(f"Loaded {len(raw_data)} rows")
        
        raw_data['Condition'] = condition_name
        
        for col in ['participant_id', 'trial_order']:
            if col in raw_data.columns:
                raw_data[col] = raw_data[col].astype(str)
        
        raw_data['Timestamp'] = pd.to_numeric(raw_data['Timestamp'], errors='coerce')
        
        return raw_data
        
    except Exception as e:
        print(f"Error loading {filename}: {e}")
        return None

def process_all_conditions():
    start_time = time.time()
    
    print("Starting data processing")
    
    all_metrics = []
    
    for condition_name, filename in DATA_FILENAMES.items():
        raw_data = load_condition_data(condition_name, filename)
        if raw_data is None:
            continue
        
        condition_metrics = process_all_trials(raw_data, condition_name)
        
        if not condition_metrics.empty:
            all_metrics.append(condition_metrics)
            
            output_file = f"trial_metrics_{condition_name}.csv"
            condition_metrics.to_csv(output_file, index=False)
            print(f"Saved {output_file}")
    
    if not all_metrics:
        print("No data processed")
        return None
    
    combined = pd.concat(all_metrics, ignore_index=True)
    combined['Condition'] = pd.Categorical(combined['Condition'], categories=CONDITION_ORDER, ordered=True)
    
    total_time = time.time() - start_time
    
    print(f"Processing complete in {total_time:.1f}s")
    print(f"Total trials: {len(combined)}")
    print(f"Participants: {combined['participant_id'].nunique()}")
    
    # Save combined results
    combined.to_csv('all_trial_metrics_combined.csv', index=False)
    print("Saved all_trial_metrics_combined.csv")
    
    return combined

def main():
    print("Starting metric calculation")
    combined = process_all_conditions()
    
    if combined is not None:
        print("\nData ready for analysis")
        print("Use all_trial_metrics_combined.csv for statistical tests")

if __name__ == "__main__":
    main()