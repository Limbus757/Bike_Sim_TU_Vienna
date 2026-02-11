"""
Streamlined data processing - single master CSV output
Enhanced for curve analysis and comprehensive statistical data
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

# Curve classification thresholds (in meters^-1)
CURVATURE_THRESHOLDS = {
    'Straight': 0.001,      # > 1000m radius
    'Gentle': 0.01,         # 100m - 1000m radius
    'Moderate': 0.05,       # 20m - 100m radius
    'Sharp': 0.5,           # 2m - 20m radius
    'Very_Sharp': float('inf')
}

def identify_continuous_curves(curvature_data, timestamps, min_samples=10):
    """
    Identify continuous curve segments from curvature data
    """
    if len(curvature_data) == 0:
        return []
    
    curves = []
    in_curve = False
    curve_start = 0
    abs_curvature = np.abs(curvature_data)
    curve_threshold = 0.001
    
    for i in range(len(abs_curvature)):
        is_curved = abs_curvature[i] > curve_threshold
        
        if is_curved and not in_curve:
            curve_start = i
            in_curve = True
        elif not is_curved and in_curve:
            curve_end = i - 1
            if curve_end - curve_start >= min_samples:
                curve_curvature = abs_curvature[curve_start:curve_end+1]
                avg_curvature = np.mean(curve_curvature)
                curve_type = classify_curve(avg_curvature)
                
                curves.append({
                    'start_idx': curve_start,
                    'end_idx': curve_end,
                    'type': curve_type,
                    'avg_curvature': avg_curvature,
                    'avg_radius': 1/avg_curvature if avg_curvature > 0 else float('inf'),
                    'duration': timestamps[curve_end] - timestamps[curve_start],
                    'samples': curve_end - curve_start + 1
                })
            in_curve = False
    
    # Handle curve that continues to end
    if in_curve and (len(abs_curvature) - 1 - curve_start) >= min_samples:
        curve_end = len(abs_curvature) - 1
        curve_curvature = abs_curvature[curve_start:curve_end+1]
        avg_curvature = np.mean(curve_curvature)
        curve_type = classify_curve(avg_curvature)
        
        curves.append({
            'start_idx': curve_start,
            'end_idx': curve_end,
            'type': curve_type,
            'avg_curvature': avg_curvature,
            'avg_radius': 1/avg_curvature if avg_curvature > 0 else float('inf'),
            'duration': timestamps[curve_end] - timestamps[curve_start],
            'samples': curve_end - curve_start + 1
        })
    
    return curves

def classify_curve(avg_curvature):
    """Classify curve based on average curvature"""
    abs_curv = abs(avg_curvature)
    
    if abs_curv <= CURVATURE_THRESHOLDS['Straight']:
        return 'Straight'
    elif abs_curv <= CURVATURE_THRESHOLDS['Gentle']:
        return 'Gentle'
    elif abs_curv <= CURVATURE_THRESHOLDS['Moderate']:
        return 'Moderate'
    elif abs_curv <= CURVATURE_THRESHOLDS['Sharp']:
        return 'Sharp'
    else:
        return 'Very_Sharp'

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

def analyze_curve_segments(trial_data, curves):
    """Analyze LKA engagement for each identified curve segment"""
    curve_analyses = []
    
    if 'LKA_Switch' not in trial_data.columns or 'LKA_Engaged' not in trial_data.columns:
        return curve_analyses
    
    timestamps = trial_data['Timestamp'].values
    switch_values = trial_data['LKA_Switch'].values
    engaged_values = trial_data['LKA_Engaged'].values
    
    for curve in curves:
        start_idx = curve['start_idx']
        end_idx = curve['end_idx']
        
        curve_timestamps = timestamps[start_idx:end_idx+1]
        curve_switch = switch_values[start_idx:end_idx+1]
        
        if len(curve_timestamps) > 1:
            time_intervals = np.diff(curve_timestamps)
            switch_on_mask = curve_switch[:-1] == 1
            switch_on_time = np.sum(time_intervals[switch_on_mask])
            total_time = curve_timestamps[-1] - curve_timestamps[0]
            
            if total_time > 0:
                curve['switch_on_pct'] = (switch_on_time / total_time) * 100
            else:
                curve['switch_on_pct'] = 0
            
            curve_analyses.append(curve)
    
    return curve_analyses

def calculate_trial_metrics(trial_data, participant_id, trial_order, condition):
    if trial_data.empty:
        return None
    
    trial_data = trial_data.sort_values('Timestamp').reset_index(drop=True)
    
    # BASIC TRIAL INFO
    results = {
        'participant_id': participant_id,
        'trial_order': trial_order,
        'Condition': condition,
        'trial_duration': trial_data['Timestamp'].iloc[-1] - trial_data['Timestamp'].iloc[0] if len(trial_data) > 0 else 0
    }
    
    # LAP TIME
    timestamps = trial_data['Timestamp'].values
    if len(timestamps) > 0:
        results['Laptime'] = timestamps[-1] - timestamps[0]
    
    # SPEED
    speed_values = trial_data['Speed_KmH'].dropna().values
    if len(speed_values) > 0:
        stats = calculate_basic_stats(speed_values)
        results.update({f'Speed_{k}': v for k, v in stats.items()})
    
    # CTE (PERFORMANCE)
    cte_values = trial_data['CTE_Meters'].dropna().values
    if len(cte_values) > 0:
        results['CTE_Std'] = float(np.std(cte_values))
        results['CTE_Abs_Mean'] = float(np.mean(np.abs(cte_values)))
        results['CTE_Abs_Median'] = float(np.median(np.abs(cte_values)))
    
    # HEADING ERROR
    heading_values = trial_data['B_HeadingError_Deg'].dropna().values
    if len(heading_values) > 0:
        results['Heading_Std'] = float(np.std(heading_values))
        results['Heading_Abs_Mean'] = float(np.mean(np.abs(heading_values)))
        results['Heading_Abs_Median'] = float(np.median(np.abs(heading_values)))
    
    # SECONDARY TASK
    sec_task = calculate_secondary_task_performance(trial_data)
    results.update(sec_task)
    
    # CURVATURE ANALYSIS (CONTINUOUS CURVES)
    if 'TrackCurvature_1/Meters' in trial_data.columns:
        curvature = trial_data['TrackCurvature_1/Meters'].values
        abs_curvature = np.abs(curvature)
        
        # Basic curvature stats
        results['Curvature_Mean'] = float(np.mean(abs_curvature))
        results['Curvature_Std'] = float(np.std(curvature))
        
        # Identify and analyze continuous curves
        curves = identify_continuous_curves(curvature, timestamps)
        results['n_curves'] = len(curves)
        
        if curves:
            # Group curves by type and calculate metrics
            curve_df = pd.DataFrame(curves)
            
            for curve_type in ['Straight', 'Gentle', 'Moderate', 'Sharp', 'Very_Sharp']:
                type_curves = curve_df[curve_df['type'] == curve_type]
                results[f'Curves_{curve_type}_count'] = len(type_curves)
                
                if len(type_curves) > 0:
                    results[f'Curves_{curve_type}_avg_radius'] = type_curves['avg_radius'].mean()
                    results[f'Curves_{curve_type}_avg_duration'] = type_curves['duration'].mean()
            
            # Analyze LKA engagement in curves
            curve_analyses = analyze_curve_segments(trial_data, curves)
            if curve_analyses:
                curve_analysis_df = pd.DataFrame(curve_analyses)
                
                for curve_type in ['Gentle', 'Moderate', 'Sharp', 'Very_Sharp']:
                    type_analyses = curve_analysis_df[curve_analysis_df['type'] == curve_type]
                    if len(type_analyses) > 0:
                        results[f'LKA_{curve_type}_switch_pct'] = type_analyses['switch_on_pct'].mean()
    
    # LKA METRICS (TRUST)
    if 'LKA_Switch' in trial_data.columns and 'LKA_Engaged' in trial_data.columns:
        switch_values = trial_data['LKA_Switch'].values
        engaged_values = trial_data['LKA_Engaged'].values
        
        if len(timestamps) > 1:
            time_intervals = np.diff(timestamps)
            total_time = timestamps[-1] - timestamps[0]
            
            # Primary trust metric: Switch ON percentage
            switch_on_mask = switch_values[:-1] == 1
            switch_on_time = np.sum(time_intervals[switch_on_mask])
            
            if total_time > 0:
                results['LKA_SwitchOn_Time_Pct'] = (switch_on_time / total_time) * 100
                results['LKA_SwitchOn_Total_Time'] = switch_on_time
            
            # Activation when switch is ON
            switch_on_engaged_mask = (switch_values[:-1] == 1) & (engaged_values[:-1] == 1)
            engaged_time = np.sum(time_intervals[switch_on_engaged_mask])
            
            if switch_on_time > 0:
                results['LKA_Activation_Pct'] = (engaged_time / switch_on_time) * 100
            
            # Switch behavior (frequency of changes)
            switch_changes = np.diff(switch_values)
            results['LKA_Switch_On_Count'] = np.sum(switch_changes == 1)
            results['LKA_Switch_Off_Count'] = np.sum(switch_changes == -1)
    
    # LKA EFFORT (only when switch is ON)
    if 'LkaNormEffort' in trial_data.columns and 'LKA_Switch' in trial_data.columns:
        lka_on_data = trial_data[trial_data['LKA_Switch'] == 1]
        effort = lka_on_data['LkaNormEffort'].dropna().values
        
        if len(effort) > 0:
            abs_effort = np.abs(effort)
            stats = calculate_basic_stats(abs_effort)
            results.update({f'LkaEffort_{k}': v for k, v in stats.items()})
        else:
            results.update({f'LkaEffort_{k}': np.nan for k in ['mean', 'median', 'std', 'cv']})
    
    # VIBRATION METRICS
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
    
    print(f"  Processing {len(grouped)} trials for {condition_name}")
    
    for (pid, order), trial_group in grouped:
        metrics = calculate_trial_metrics(trial_group, pid, order, condition_name)
        if metrics:
            all_metrics.append(metrics)
    
    if all_metrics:
        df = pd.DataFrame(all_metrics)
        print(f"  ✓ Processed {len(df)} trials")
        return df
    return pd.DataFrame()

def find_data_file(filename):
    for path in DATA_SEARCH_PATHS:
        full_path = os.path.join(path, filename)
        if os.path.exists(full_path):
            return full_path
    return None

def load_condition_data(condition_name, filename):
    print(f"Loading {condition_name}...")
    
    file_path = find_data_file(filename)
    if not file_path:
        print(f"  ✗ File not found: {filename}")
        return None
    
    try:
        raw_data = pd.read_csv(file_path, sep=';', decimal=',')
        print(f"  ✓ Loaded {len(raw_data)} rows")
        
        raw_data['Condition'] = condition_name
        
        for col in ['participant_id', 'trial_order']:
            if col in raw_data.columns:
                raw_data[col] = raw_data[col].astype(str)
        
        raw_data['Timestamp'] = pd.to_numeric(raw_data['Timestamp'], errors='coerce')
        
        return raw_data
        
    except Exception as e:
        print(f"  ✗ Error loading {filename}: {e}")
        return None

def create_master_csv(combined_df):
    """
    Create a single master CSV with all data needed for statistical analysis
    """
    if combined_df.empty:
        return None
    
    # Create output directory
    os.makedirs('analysis_output', exist_ok=True)
    
    # Add timestamp for unique filename
    timestamp = time.strftime("%Y%m%d_%H%M%S")
    filename = f"analysis_output/master_analysis_data_{timestamp}.csv"
    
    # Sort for readability
    combined_df = combined_df.sort_values(['participant_id', 'trial_order', 'Condition'])
    
    # Save to CSV
    combined_df.to_csv(filename, index=False)
    
    # Create a simple summary file
    summary_content = f"""# MASTER ANALYSIS DATA
Generated: {time.strftime("%Y-%m-%d %H:%M:%S")}
File: {filename}

## DATA OVERVIEW
Total trials: {len(combined_df)}
Unique participants: {combined_df['participant_id'].nunique()}
Conditions: {', '.join(combined_df['Condition'].unique())}

## KEY METRICS FOR TRUST ANALYSIS
1. LKA_SwitchOn_Time_Pct - Primary trust metric (% time switch ON)
2. LkaEffort_mean - Steering effort when LKA active (inverse trust)
3. CTE_Std - Performance consistency
4. CorrectedAcc - Secondary task performance

## CURVATURE ANALYSIS METRICS
- Curves_*_count: Number of curves of each type
- Curves_*_avg_radius: Average radius of curves
- LKA_*_switch_pct: LKA engagement during specific curve types

## SUGGESTED STATISTICAL TESTS
1. Repeated measures ANOVA on LKA_SwitchOn_Time_Pct across conditions
2. Correlation between LKA engagement and performance metrics
3. Mixed-effects modeling for curvature effects

## COLUMNS INCLUDED ({len(combined_df.columns)} total):
{', '.join(sorted(combined_df.columns.tolist()))}
"""
    
    with open(f"analysis_output/README_{timestamp}.txt", 'w') as f:
        f.write(summary_content)
    
    return filename, timestamp

def main():
    print("=" * 60)
    print("MASTER DATA PROCESSING FOR TRUST & CURVATURE ANALYSIS")
    print("=" * 60)
    
    start_time = time.time()
    
    all_metrics = []
    
    for condition_name, filename in DATA_FILENAMES.items():
        print(f"\nProcessing {condition_name}...")
        raw_data = load_condition_data(condition_name, filename)
        
        if raw_data is None:
            continue
        
        condition_metrics = process_all_trials(raw_data, condition_name)
        
        if not condition_metrics.empty:
            all_metrics.append(condition_metrics)
    
    if not all_metrics:
        print("\n✗ No data processed successfully")
        return
    
    # Combine all data
    combined = pd.concat(all_metrics, ignore_index=True)
    combined['Condition'] = pd.Categorical(combined['Condition'], categories=CONDITION_ORDER, ordered=True)
    
    # Create master CSV
    print("\n" + "=" * 60)
    print("CREATING MASTER CSV FILE...")
    master_file, timestamp = create_master_csv(combined)
    
    total_time = time.time() - start_time
    
    print("\n" + "=" * 60)
    print("PROCESSING COMPLETE!")
    print("=" * 60)
    print(f"✓ Total time: {total_time:.1f}s")
    print(f"✓ Total trials: {len(combined)}")
    print(f"✓ Unique participants: {combined['participant_id'].nunique()}")
    print(f"✓ Master file: {master_file}")
    
    # Show key metrics summary
    print("\nKEY METRICS SUMMARY (mean ± SD):")
    print("-" * 50)
    
    key_metrics = {
        'TRUST': ['LKA_SwitchOn_Time_Pct', 'LKA_Activation_Pct'],
        'PERFORMANCE': ['CTE_Std', 'Heading_Std'],
        'SECONDARY TASK': ['CorrectedAcc']
    }
    
    for category, metrics in key_metrics.items():
        print(f"\n{category}:")
        for metric in metrics:
            if metric in combined.columns:
                valid_data = combined[metric].dropna()
                if len(valid_data) > 0:
                    mean_val = np.mean(valid_data)
                    std_val = np.std(valid_data)
                    print(f"  {metric}: {mean_val:.2f} ± {std_val:.2f}")
    
    print("\n" + "=" * 60)
    print("NEXT STEPS:")
    print("=" * 60)
    print("1. Run statistical analysis on master_analysis_data_{timestamp}.csv")
    print("2. Focus on LKA_SwitchOn_Time_Pct as primary trust metric")
    print("3. Use 'Condition' as within-subjects factor for ANOVA")
    print("\nExample analysis script:")
    print("   import pandas as pd")
    print("   import pingouin as pg")
    print("   data = pd.read_csv('{}')".format(master_file))
    print("   # Run repeated measures ANOVA:")
    print("   anova = pg.rm_anova(data=data, dv='LKA_SwitchOn_Time_Pct',")
    print("                         within='Condition', subject='participant_id')")
    print("   print(anova)")

if __name__ == "__main__":
    main()