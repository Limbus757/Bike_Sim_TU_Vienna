import pandas as pd
import glob
import os
import re

# File paths
input_folder = 'data_input'
output_base_folder = 'processed_output'

output_subdirectories = {
    "normalized": os.path.join(output_base_folder, "1_Normalized_Originals"),
    "by_participant": os.path.join(output_base_folder, "2_By_Participant"),
    "by_condition": os.path.join(output_base_folder, "3_By_Condition"),
    "by_order": os.path.join(output_base_folder, "4_By_Trial_Order")
}

for directory_path in output_subdirectories.values():
    os.makedirs(directory_path, exist_ok=True)

raw_file_list = glob.glob(os.path.join(input_folder, "*.csv"))
processed_dataframes_list = []

print(f"Processing {len(raw_file_list)} files...\n")

# --- Load and Clean ---
for file_path in raw_file_list:
    try:
        # Save study info header
        with open(file_path, 'r', encoding='utf-8') as file_connection:
            study_info_header = file_connection.readline().strip()

        # Load as string to handle comma/dot swap
        trial_data = pd.read_csv(file_path, sep=';', skiprows=1, dtype=str)
        
        # Strip footer and debug columns
        trial_data = trial_data.iloc[:-1, :-3]

        # Convert to numeric format
        trial_data = trial_data.stack().str.replace(',', '.').unstack()
        trial_data = trial_data.apply(pd.to_numeric, errors='coerce')

        # Reset timer to zero
        timestamp_column_name = trial_data.columns[0]
        trial_data[timestamp_column_name] = trial_data[timestamp_column_name] - trial_data[timestamp_column_name].iloc[0]
        
        # Filename metadata extraction
        filename_base = os.path.basename(file_path)
        filename_no_extension = filename_base.replace('.csv', '')
        filename_parts = filename_no_extension.split('_')
        
        participant_id = int(filename_parts[0]) 
        raw_condition_name = filename_parts[1]
        clean_condition_name = re.sub(r'[A-Z]{2}$', '', raw_condition_name) 
        file_timestamp_string = f"{filename_parts[2]}_{filename_parts[3]}"
        
        # Export normalized version immediately
        normalized_export_path = os.path.join(output_subdirectories["normalized"], filename_base)
        with open(normalized_export_path, 'w', encoding='utf-8') as export_connection:
            export_connection.write(study_info_header + "\n")
            trial_data.to_csv(export_connection, sep=';', decimal=',', index=False, float_format='%.4f')
        
        # Store for combined processing
        trial_data['participant_id'] = participant_id
        trial_data['trial_condition'] = clean_condition_name
        
        processed_dataframes_list.append((participant_id, file_timestamp_string, trial_data))
        
    except Exception as error_message:
        print(f"Error in {file_path}: {error_message}")

if not processed_dataframes_list:
    print("\nNo data found.")
else:
    # --- Sorting and Sequential Ordering ---
    processed_dataframes_list.sort(key=lambda x: (x[0], x[1]))

    final_merged_list = []
    current_participant_id = None
    participant_trial_counter = 0

    for participant_id, timestamp, trial_dataframe in processed_dataframes_list:
        if participant_id != current_participant_id:
            current_participant_id = participant_id
            participant_trial_counter = 1 
        else:
            participant_trial_counter += 1 
        
        trial_dataframe['trial_order'] = participant_trial_counter
        
        # Ensure integers stay integers
        trial_dataframe['participant_id'] = trial_dataframe['participant_id'].astype(int)
        trial_dataframe['trial_order'] = trial_dataframe['trial_order'].astype(int)
        
        # Sort column order
        metadata_columns = ['participant_id', 'trial_condition', 'trial_order']
        ordered_columns = metadata_columns + [col for col in trial_dataframe.columns if col not in metadata_columns]
        trial_dataframe = trial_dataframe[ordered_columns]
        final_merged_list.append(trial_dataframe)

    # --- Final Exports ---
    master_dataframe = pd.concat(final_merged_list, axis=0, ignore_index=True)

    def export_csv_file(dataframe_object, export_path):
        dataframe_object.to_csv(export_path, sep=';', decimal=',', index=False, float_format='%.4f')

    for p_id, participant_group in master_dataframe.groupby('participant_id'):
        export_csv_file(participant_group, os.path.join(output_subdirectories["by_participant"], f'participant_{p_id}.csv'))

    for condition, condition_group in master_dataframe.groupby('trial_condition'):
        export_csv_file(condition_group, os.path.join(output_subdirectories["by_condition"], f'condition_{condition}.csv'))

    for order_index, order_group in master_dataframe.groupby('trial_order'):
        export_csv_file(order_group, os.path.join(output_subdirectories["by_order"], f'order_{order_index}.csv'))

    export_csv_file(master_dataframe, os.path.join(output_base_folder, 'everything_combined.csv'))

    print("-" * 30)
    print("Processing complete.")
    print(f"Total trials: {len(processed_dataframes_list)}")