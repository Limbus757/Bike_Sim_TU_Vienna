"""
Streamlined data processing - single master CSV output
Enhanced for curve analysis and comprehensive statistical data
"""
import pandas as pd
import numpy as np
import os
import time

# --- Configuration ---
CONDITION_ORDER = ['Baseline', 'HapticsFixed', 'HapticsAdaptive']
DATA_FILENAMES = {
    'Baseline': 'Baseline.csv',
    'HapticsFixed': 'HapticsFixed.csv', 
    'HapticsAdaptive': 'HapticsAdaptive.csv'
}

# Updated to match your actual output directory from the previous script
DATA_SEARCH_PATHS = ["grouped_cleaned_output/grouped_by_condition"]

# Curve classification thresholds (in meters^-1)
CURVATURE_THRESHOLDS = {
    'Straight': 0.001,      # > 1000m radius
    'Gentle': 0.01,         # 100m - 1000m radius
    'Moderate': 0.05,       # 20m - 100m radius
    'Sharp': 0.5,           # 2m - 20m radius
    'Very_Sharp': float('inf')
}

# --- Core Logic ---

def calculate_basic_stats(values):
    """Calculates stats with safety conversion to prevent TypeError."""
    if values is None or len(values) == 0:
        return {'mean': np.nan, 'median': np.nan, 'std': np.nan, 'cv': np.nan}
    
    # Force numeric conversion and drop NaNs/Infs
    v = pd.to_numeric(values, errors='coerce')
    v = v[np.isfinite(v)]
    
    if len(v) == 0:
        return {'mean': np.nan, 'median': np.nan, 'std': np.nan, 'cv': np.nan}
    
    mean_val = np.mean(v)
    std_val = np.std(v)
    
    return {
        'mean': float(mean_val),
        'median': float(np.median(v)),
        'std': float(std_val),
        'cv': float(std_val / mean_val) if mean_val != 0 else np.nan
    }

def classify_curve(avg_curvature):
    abs_curv = abs(avg_curvature)
    if abs_curv <= CURVATURE_THRESHOLDS['Straight']: return 'Straight'
    elif abs_curv <= CURVATURE_THRESHOLDS['Gentle']: return 'Gentle'
    elif abs_curv <= CURVATURE_THRESHOLDS['Moderate']: return 'Moderate'
    elif abs_curv <= CURVATURE_THRESHOLDS['Sharp']: return 'Sharp'
    else: return 'Very_Sharp'

def identify_continuous_curves(curvature_data, timestamps, min_samples=10):
    if len(curvature_data) == 0: return []
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
                avg_curvature = np.mean(abs_curvature[curve_start:curve_end+1])
                curves.append({
                    'start_idx': curve_start, 'end_idx': curve_end,
                    'type': classify_curve(avg_curvature),
                    'avg_curvature': avg_curvature,
                    'avg_radius': 1/avg_curvature if avg_curvature > 0 else float('inf'),
                    'duration': timestamps[curve_end] - timestamps[curve_start],
                    'samples': curve_end - curve_start + 1
                })
            in_curve = False
    return curves

def calculate_secondary_task_performance(trial_data, response_window=1.5):
    trial_data = trial_data.sort_values('Timestamp').reset_index(drop=True)
    ts, target_flags, press_flags = trial_data['Timestamp'].values, trial_data['SecTaskIsTarget'].values, trial_data['SecTaskPressed'].values
    
    stim_changes = []
    for i in range(len(trial_data)):
        if target_flags[i] != -1:
            if i == 0 or target_flags[i] != target_flags[i-1]:
                stim_changes.append((ts[i], target_flags[i]))
    
    if not stim_changes: return {'HitRate': 0, 'FARate': 0, 'CorrectedAcc': 0}
    
    target_times = [t for t, f in stim_changes if f == 1]
    distractor_count = sum(1 for _, f in stim_changes if f == 0)
    press_times = [ts[i] for i in range(1, len(trial_data)) if press_flags[i] == 1 and press_flags[i-1] == 0]
    
    hits, used_presses = 0, set()
    for t_time in target_times:
        for idx, p_time in enumerate(press_times):
            if idx not in used_presses and t_time <= p_time <= (t_time + response_window):
                used_presses.add(idx)
                hits += 1
                break
    
    hit_rate = (hits / len(target_times)) * 100 if target_times else 0
    false_alarms = len(press_times) - len(used_presses)
    fa_rate = (false_alarms / distractor_count) * 100 if distractor_count > 0 else 0
    return {'HitRate': hit_rate, 'FARate': fa_rate, 'CorrectedAcc': hit_rate - fa_rate}

def analyze_curve_segments(trial_data, curves):
    curve_analyses = []
    if 'LKA_Switch' not in trial_data.columns: return []
    ts, sw = trial_data['Timestamp'].values, trial_data['LKA_Switch'].values
    
    for curve in curves:
        s, e = curve['start_idx'], curve['end_idx']
        c_ts, c_sw = ts[s:e+1], sw[s:e+1]
        if len(c_ts) > 1:
            total_time = c_ts[-1] - c_ts[0]
            sw_on_time = np.sum(np.diff(c_ts)[c_sw[:-1] == 1])
            curve['switch_on_pct'] = (sw_on_time / total_time * 100) if total_time > 0 else 0
            curve_analyses.append(curve)
    return curve_analyses

def calculate_trial_metrics(trial_data, pid, trial_order, condition):
    if trial_data.empty: return None
    trial_data = trial_data.sort_values('Timestamp').reset_index(drop=True)
    ts = trial_data['Timestamp'].values
    results = {
        'participant_id': pid, 'trial_order': trial_order, 'Condition': condition,
        'Laptime': ts[-1] - ts[0] if len(ts) > 0 else 0
    }
    
    # Performance Stats
    for col, prefix in [('Speed_KmH', 'Speed'), ('CTE_Meters', 'CTE_Abs'), ('B_HeadingError_Deg', 'Heading_Abs')]:
        if col in trial_data.columns:
            vals = trial_data[col].dropna().values
            if 'Abs' in prefix: vals = np.abs(vals)
            stats = calculate_basic_stats(vals)
            results.update({f'{prefix}_{k}': v for k, v in stats.items()})

    results.update(calculate_secondary_task_performance(trial_data))

    # Curvature
    if 'TrackCurvature_1/Meters' in trial_data.columns:
        curv = trial_data['TrackCurvature_1/Meters'].values
        curves = identify_continuous_curves(curv, ts)
        results['n_curves'] = len(curves)
        if curves:
            c_df = pd.DataFrame(curves)
            for t in ['Straight', 'Gentle', 'Moderate', 'Sharp']:
                t_c = c_df[c_df['type'] == t]
                results[f'Curves_{t}_count'] = len(t_c)
                if len(t_c) > 0: results[f'Curves_{t}_avg_duration'] = t_c['duration'].mean()

            c_ans = analyze_curve_segments(trial_data, curves)
            if c_ans:
                ca_df = pd.DataFrame(c_ans)
                for t in ['Gentle', 'Moderate', 'Sharp']:
                    t_ans = ca_df[ca_df['type'] == t]
                    if not t_ans.empty: results[f'LKA_{t}_switch_pct'] = t_ans['switch_on_pct'].mean()

    # LKA Trust
    if 'LKA_Switch' in trial_data.columns:
        sw, eng = trial_data['LKA_Switch'].values, trial_data['LKA_Engaged'].values
        if len(ts) > 1:
            diffs, total_t = np.diff(ts), ts[-1] - ts[0]
            sw_on_t = np.sum(diffs[sw[:-1] == 1])
            results['LKA_SwitchOn_Time_Pct'] = (sw_on_t / total_t * 100) if total_t > 0 else 0
            results['LKA_Switch_On_Count'] = np.sum(np.diff(sw) == 1)

    return results

# --- Main Functions ---

def load_condition_data(condition_name, filename):
    print(f"Loading {condition_name}...")
    file_path = None
    for p in DATA_SEARCH_PATHS:
        full = os.path.join(p, filename)
        if os.path.exists(full):
            file_path = full; break
    
    if not file_path:
        print(f"  ✗ File not found: {filename}"); return None
    
    try:
        # FIXED: Using decimal='.' because your output uses dots
        raw_data = pd.read_csv(file_path, sep=';', decimal='.')
        print(f"  ✓ Loaded {len(raw_data)} rows")
        
        # Numeric safety on load
        for col in ['participant_id', 'trial_order', 'Timestamp', 'Speed_KmH', 'CTE_Meters']:
            if col in raw_data.columns:
                raw_data[col] = pd.to_numeric(raw_data[col], errors='coerce')
        
        return raw_data
    except Exception as e:
        print(f"  ✗ Error loading: {e}"); return None

def main():
    print("=" * 60)
    print("MASTER DATA PROCESSING FOR TRUST & CURVATURE ANALYSIS")
    print("=" * 60)
    
    start_time = time.time()
    all_metrics = []
    
    for condition_name, filename in DATA_FILENAMES.items():
        raw_data = load_condition_data(condition_name, filename)
        if raw_data is None: continue
        
        # Group and process
        grouped = raw_data.groupby(['participant_id', 'trial_order'])
        print(f"  Processing {len(grouped)} trials for {condition_name}")
        
        for (pid, order), trial_group in grouped:
            m = calculate_trial_metrics(trial_group, pid, order, condition_name)
            if m: all_metrics.append(m)
    
    if not all_metrics:
        print("\n✗ No data processed successfully"); return

    combined = pd.DataFrame(all_metrics)
    os.makedirs('analysis_output', exist_ok=True)
    ts_str = time.strftime("%Y%m%d_%H%M%S")
    fname = f"analysis_output/master_analysis_data_{ts_str}.csv"
    combined.to_csv(fname, index=False)
    
    print("\n" + "=" * 60)
    print("PROCESSING COMPLETE!")
    print(f"✓ Master file: {fname}")
    print(f"✓ Total Runtime: {time.time() - start_time:.1f}s")
    print("=" * 60)

if __name__ == "__main__":
    main()