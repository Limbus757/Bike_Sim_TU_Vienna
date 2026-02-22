import pandas as pd
import numpy as np
import os

# ==========================================
# FILE & DIRECTORY SETTINGS
# ==========================================
# Sources
METRICS_DIR = os.path.join("..", "02_extracted_trial_metrics")
SURVEY_DIR  = os.path.join("..", "00_raw_questionare_data")

# Output folder
OUTPUT_DIR  = os.path.join("..", "03_aggregated_data")

# Filenames
FILE_METRICS    = os.path.join(METRICS_DIR, 'logged_trial_metrics.csv')
FILE_SURVEY1    = os.path.join(SURVEY_DIR, 'Official Data Collection - Form responses 1.csv')
FILE_SURVEY2    = os.path.join(SURVEY_DIR, 'Official Data Collection - Form responses 2.csv')

# Final Aggregated Output Filename
AGGREGATED_OUT  = 'fully_combined_metrics_aggregated.csv'

if not os.path.exists(OUTPUT_DIR):
    os.makedirs(OUTPUT_DIR)

# ==========================================
# 1. LOAD DATA
# ==========================================
print("Loading datasets...")
df_metrics = pd.read_csv(FILE_METRICS)
df_survey1 = pd.read_csv(FILE_SURVEY1)
df_survey2 = pd.read_csv(FILE_SURVEY2)

# Clean headers for both surveys
df_survey1.columns = df_survey1.columns.str.strip()
df_survey2.columns = df_survey2.columns.str.strip()

# ==========================================
# 2. SURVEY 2 CALCULATIONS (Trust & TAM)
# ==========================================
print("Calculating Trust and TAM scores...")
t_pos_idx = ["1.", "2.", "4.", "6.", "7.", "9.", "11.", "12."]
t_neg_idx = ["3.", "5.", "8.", "10."]

t_pos_cols = [c for c in df_survey2.columns if any(c.startswith(n) for n in t_pos_idx)]
t_neg_cols = [c for c in df_survey2.columns if any(c.startswith(n) for n in t_neg_idx)]

trust_calc = df_survey2[t_pos_cols + t_neg_cols].apply(pd.to_numeric, errors='coerce').copy()
for col in t_neg_cols:
    trust_calc[col] = 6 - trust_calc[col]

df_survey2['Trust_Score'] = trust_calc.mean(axis=1)

pu_cols = [c for c in df_survey2.columns if any(c.startswith(n + ".") for n in ["13", "14", "15", "16", "17"])]
peou_cols = [c for c in df_survey2.columns if any(c.startswith(n + ".") for n in ["18", "19", "20", "21", "22"])]

df_survey2['TAM_PU'] = df_survey2[pu_cols].apply(pd.to_numeric, errors='coerce').mean(axis=1)
df_survey2['TAM_PEOU'] = df_survey2[peou_cols].apply(pd.to_numeric, errors='coerce').mean(axis=1)
df_survey2['TAM_Total'] = df_survey2[pu_cols + peou_cols].apply(pd.to_numeric, errors='coerce').mean(axis=1)

# ==========================================
# 3. INITIAL MERGE (Trial-Level Performance + Survey 2)
# ==========================================
df_survey2['Participant ID'] = pd.to_numeric(df_survey2['Participant ID'], errors='coerce').fillna(0).astype(int)
df_survey2['Trial.No']       = pd.to_numeric(df_survey2['Trial.No'], errors='coerce').fillna(0).astype(int)

df_metrics['participant_id'] = df_metrics['participant_id'].astype(int)
df_metrics['trial_order']    = df_metrics['trial_order'].astype(int)

df_combined = pd.merge(
    df_metrics,
    df_survey2[['Participant ID', 'Trial.No', 'Trust_Score', 'TAM_PU', 'TAM_PEOU', 'TAM_Total']],
    left_on=['participant_id', 'trial_order'],
    right_on=['Participant ID', 'Trial.No'],
    how='left'
).drop(columns=['Participant ID', 'Trial.No'])

# ==========================================
# 4. AGGREGATION (Condition Level)
# ==========================================
print("Aggregating metrics per participant/condition...")
meta_cols = ['participant_id', 'condition']
metrics_cols = [c for c in df_combined.columns if c not in meta_cols]

def rms(x):
    return np.sqrt(np.mean(np.square(pd.to_numeric(x, errors='coerce'))))

# RMS for SD/variability columns, Mean for others
agg_logic = {
    col: (rms if any(k in col.lower() for k in ['_sd', 'variability']) else 'mean') 
    for col in metrics_cols
}

df_aggregated = df_combined.groupby(meta_cols, observed=True).agg(agg_logic).reset_index()

# Drop trial_order as it's not relevant after aggregation
if 'trial_order' in df_aggregated.columns:
    df_aggregated = df_aggregated.drop(columns=['trial_order'])

# ==========================================
# 5. MERGE DEMOGRAPHICS (Survey 1)
# ==========================================
print("Matching demographics from Form 1...")
# Identify which demographic columns to keep
demo_cols = [
    'Participant ID', 
    'Specify what gender you identify with', 
    'How old are you?',
    'How often do you ride a bicycle?', 
    'How confident are you in your cycling skills?',
    'Have you used VR systems or simulators before?', 
    'If yes, how often do you use VR or simulators?',
    'How familiar are you with VR or simulation environments?'
]

# Ensure Participant ID is cleaned in Survey 1
df_survey1['Participant ID'] = pd.to_numeric(df_survey1['Participant ID'], errors='coerce').fillna(0).astype(int)

# Select only present columns to avoid errors
demo_present = [c for c in demo_cols if c in df_survey1.columns]

# Final Merge (adds demographics to the aggregated rows)
df_final = pd.merge(
    df_aggregated,
    df_survey1[demo_present],
    left_on='participant_id',
    right_on='Participant ID',
    how='left'
).drop(columns=['Participant ID'])

# ==========================================
# 6. SAVE OUTPUT
# ==========================================
output_path = os.path.join(OUTPUT_DIR, AGGREGATED_OUT)
df_final.round(4).to_csv(output_path, index=False)

print("-" * 30)
print(f"SUCCESS!")
print(f"Aggregated file with demographics saved to: {output_path}")
print(f"Unique Participants: {df_final['participant_id'].nunique()}")