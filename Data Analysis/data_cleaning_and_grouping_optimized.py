import pandas as pd
import numpy as np
import glob
import os
import time
import sys
from concurrent.futures import ThreadPoolExecutor, as_completed
from io import StringIO
import pyarrow as pa
import pyarrow.csv as pc

# --- Configuration ---
INPUT_DIRS = ['.', '_raw_data_input']
OUTPUT_DIR = 'grouped_cleaned_output'
GROUP_SUBDIR = os.path.join(OUTPUT_DIR, 'grouped_by_condition')
NORM_SUBDIR = os.path.join(OUTPUT_DIR, '_normalized_laps')
INCLUDE_INDICES = [0, 1, 2, 3, 5, 6, 8, 10, 11, 13, 14, 16, 17, 19, 20, 21]

# --- Utilities ---

def save_csv_arrow(df, path):
    """Saves DataFrame to CSV using PyArrow's multi-threaded engine."""
    try:
        table = pa.Table.from_pandas(df, preserve_index=False)
        write_options = pc.WriteOptions(include_header=True, delimiter=';', batch_size=1024*512)
        with pa.OSFile(path, 'wb') as f:
            pc.write_csv(table, f, write_options=write_options)
        return True
    except Exception:
        return False

def process_file_fast(path):
    """Reads raw data, normalizes timestamps, and extracts file-level metadata."""
    try:
        fname = os.path.basename(path)
        with open(path, 'r', encoding='utf-8') as f:
            lines = f.readlines()
        
        if len(lines) < 2: return None
        header_metadata = lines[0].strip()
        
        # Strip trailing log entries
        if "---" in lines[-1] or "LOG" in lines[-1]: 
            lines = lines[:-1]
        
        df = pd.read_csv(
            StringIO("".join(lines[1:])), 
            sep=';', 
            decimal=',', 
            usecols=INCLUDE_INDICES, 
            engine='c'
        )
        
        if df.empty: return None

        # Rebase timestamps to start at 0.0 and round numerical columns
        ts_values = df.iloc[:, 0].values
        df.iloc[:, 0] = (ts_values - ts_values[0]).round(4)
        num_cols = df.select_dtypes(include=[np.float64, np.float32]).columns
        df[num_cols] = df[num_cols].round(4)

        # Parse filename parts: ID_Condition_Timestamp
        parts = fname.replace('.csv', '').split('_')
        df['_internal_pid'] = np.int16(parts[0])
        df['_internal_cond_full'] = parts[1]
        # Identify base condition for file grouping (strip CC/CW)
        df['_internal_cond_base'] = parts[1].replace('CC', '').replace('CW', '')
        df['_internal_ts'] = parts[2] + "_" + parts[3]
        df['_internal_fname'] = fname
        df['_internal_hdr'] = header_metadata
        return df
    except Exception:
        return None

def save_individual_worker(df, output_dir, final_cols):
    """Writes individual lap files with the original data headers."""
    try:
        fn = df['_internal_fname'].iloc[0]
        hdr = df['_internal_hdr'].iloc[0]
        export_df = df[final_cols]
        path = os.path.join(output_dir, fn)
        with open(path, 'w', encoding='utf-8') as f:
            f.write(hdr + "\n")
            export_df.to_csv(f, sep=';', decimal=',', index=False)
        return True
    except Exception:
        return False

# --- Main Pipeline ---

def main():
    metrics = {}
    t_start = time.time()
    for d in [GROUP_SUBDIR, NORM_SUBDIR]: os.makedirs(d, exist_ok=True)
    
    # Discovery
    s_step = time.time()
    files = []
    for d in INPUT_DIRS:
        if os.path.exists(d):
            found = glob.glob(os.path.join(d, "*.csv"))
            # Filter for standard participant filenames
            files.extend([f for f in found if os.path.basename(f)[0].isdigit() and "_" in f])
    files = sorted(list(set(files)))
    metrics['Discovery'] = time.time() - s_step

    if not files:
        print("No input files found.")
        return

    # Processing
    print(f"Reading {len(files)} files...")
    s_step = time.time()
    all_data = []
    with ThreadPoolExecutor() as pool:
        futures = [pool.submit(process_file_fast, f) for f in files]
        for fut in as_completed(futures):
            res = fut.result()
            if res is not None: all_data.append(res)
    metrics['Processing'] = time.time() - s_step

    # Formatting
    s_step = time.time()
    master = pd.concat(all_data, ignore_index=True)
    master.sort_values(['_internal_pid', '_internal_ts', master.columns[0]], inplace=True)
    
    # Define primary metadata columns
    master['participant_id'] = master['_internal_pid']
    master['condition'] = master['_internal_cond_full']
    master['trial_order'] = master.groupby('_internal_pid')['_internal_ts'].transform(
        lambda x: x.ne(x.shift()).cumsum()
    )

    # Reorder columns: ID, Condition, TrialOrder, then sensor data
    id_cols = ['participant_id', 'condition', 'trial_order']
    sensor_cols = [c for c in master.columns if not c.startswith('_internal') and c not in id_cols]
    final_cols = id_cols + sensor_cols

    # Synchronize trial_order with individual source lists
    order_map = master[['_internal_fname', 'trial_order']].drop_duplicates().set_index('_internal_fname')['trial_order'].to_dict()
    for df in all_data:
        df['participant_id'] = df['_internal_pid']
        df['condition'] = df['_internal_cond_full']
        df['trial_order'] = order_map.get(df['_internal_fname'].iloc[0], 1)

    master_export = master[final_cols]
    metrics['Consolidation'] = time.time() - s_step

    # Output Generation
    print("Writing output files...")
    s_step = time.time()
    with ThreadPoolExecutor() as writer_pool:
        # master combined file
        futs = [writer_pool.submit(save_csv_arrow, master_export, os.path.join(OUTPUT_DIR, "everything_combined.csv"))]
        
        # condition-based groups (files named by base condition)
        for base_cond, group in master.groupby('_internal_cond_base'):
            c_path = os.path.join(GROUP_SUBDIR, f"{base_cond}.csv")
            futs.append(writer_pool.submit(save_csv_arrow, group[final_cols], c_path))
            
        # individual normalized files
        futs += [writer_pool.submit(save_individual_worker, df, NORM_SUBDIR, final_cols) for df in all_data]

        for _ in as_completed(futs): pass 
    metrics['Writing'] = time.time() - s_step

    # Final Report
    unique_pids = sorted(master['participant_id'].unique())
    print("\n" + "="*50)
    print("PROCESSING REPORT")
    print("="*50)
    print(f"Total Participants:   {len(unique_pids)}")
    print(f"Participant IDs:      {', '.join(map(str, unique_pids))}")
    print(f"Total Files Processed:{len(all_data)}")
    print(f"Total Runtime:        {time.time() - t_start:.2f}s")
    print("-"*50)
    for step, dur in metrics.items():
        print(f"{step:<22}: {dur:.2f}s")
    print("="*50 + "\n")

if __name__ == "__main__":
    try:
        main()
        sys.exit(0)
    except Exception as e:
        print(f"Critical error: {e}")
        sys.exit(1)