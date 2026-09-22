import pandas as pd
import numpy as np
import os

# ==========================================
# FILE & DIRECTORY SETTINGS
# ==========================================
METRICS_DIR = os.path.join("..", "02_extracted_trial_metrics")
SURVEY_DIR  = os.path.join("..", "00_raw_questionare_data")
OUTPUT_DIR  = os.path.join("..", "03_combined_questionare_data")

FILE_METRICS    = os.path.join(METRICS_DIR, 'logged_trial_metrics.csv')
FILE_SURVEY1    = os.path.join(SURVEY_DIR, 'Official Data Collection - Form responses 1.csv')
FILE_SURVEY2    = os.path.join(SURVEY_DIR, 'Official Data Collection - Form responses 2.csv')

MERGED_OUT      = 'merged_trial_level_metrics.csv'

if not os.path.exists(OUTPUT_DIR):
    os.makedirs(OUTPUT_DIR)

# ==========================================
# 1. LOAD DATA
# ==========================================
print("Loading datasets...")
df_metrics = pd.read_csv(FILE_METRICS)
df_survey1 = pd.read_csv(FILE_SURVEY1)
df_survey2 = pd.read_csv(FILE_SURVEY2)

df_survey1.columns = df_survey1.columns.str.strip()
df_survey2.columns = df_survey2.columns.str.strip()

# ==========================================
# 2. SURVEY 2: CALCULATED SCORES ONLY
# ==========================================
print("Processing Survey 2 (Trust & TAM Scores)...")

t_pos_idx = ["1.", "2.", "4.", "6.", "7.", "9.", "11.", "12."]
t_neg_idx = ["3.", "5.", "8.", "10."]

t_pos_cols = [c for c in df_survey2.columns if any(c.startswith(n) for n in t_pos_idx)]
t_neg_cols = [c for c in df_survey2.columns if any(c.startswith(n) for n in t_neg_idx)]
pu_cols    = [c for c in df_survey2.columns if any(c.startswith(n + ".") for n in ["13", "14", "15", "16", "17"])]
peou_cols  = [c for c in df_survey2.columns if any(c.startswith(n + ".") for n in ["18", "19", "20", "21", "22"])]

trust_calc = df_survey2[t_pos_cols + t_neg_cols].apply(pd.to_numeric, errors='coerce').copy()
for col in t_neg_cols:
    trust_calc[col] = 6 - trust_calc[col]

df_survey2['Trust_Score'] = trust_calc.mean(axis=1)
df_survey2['TAM_PU'] = df_survey2[pu_cols].apply(pd.to_numeric, errors='coerce').mean(axis=1)
df_survey2['TAM_PEOU'] = df_survey2[peou_cols].apply(pd.to_numeric, errors='coerce').mean(axis=1)
df_survey2['TAM_Total'] = df_survey2[pu_cols + peou_cols].apply(pd.to_numeric, errors='coerce').mean(axis=1)

# Renaming map for Q2
q2_rename = {
    'Trust_Score': 'q2_trust',
    'TAM_PU': 'q2_tam_pu',
    'TAM_PEOU': 'q2_tam_peou',
    'TAM_Total': 'q2_tam_total'
}

# ==========================================
# 3. SURVEY 1: DEMOGRAPHICS & INTEGER MAPPING
# ==========================================
print("Processing Survey 1 Demographics...")

bike_map = {'Never': 0, 'Rarely': 1, 'Several times per month': 2, 'Several times per week': 3, 'Daily': 4}
vr_freq_map = {'This is my first time': 0, 'Rarely': 1, 'Occasionally (monthly)': 2, 'Occasionally (weekly or more)': 3}
gender_map = {'Female': 0, 'Male': 1, 'Non-binary / third gender': 2, 'Prefer not to say': 3}
vr_ever_map = {'No': 0, 'Yes': 1}

df_survey1['gender_int'] = df_survey1['Specify what gender you identify with'].map(gender_map)
df_survey1['bicycle_freq_int'] = df_survey1['How often do you ride a bicycle?'].map(bike_map)
df_survey1['vr_ever_int'] = df_survey1['Have you used VR systems or simulators before?'].map(vr_ever_map)
df_survey1['vr_freq_int'] = df_survey1['If yes, how often do you use VR or simulators?'].map(vr_freq_map)
df_survey1.loc[df_survey1['vr_ever_int'] == 0, 'vr_freq_int'] = 0
df_survey1['age_int'] = pd.to_numeric(df_survey1['How old are you?'], errors='coerce')
df_survey1['cycling_confidence_int'] = pd.to_numeric(df_survey1['How confident are you in your cycling skills?'], errors='coerce')
df_survey1['vr_familiarity_int'] = pd.to_numeric(df_survey1['How familiar are you with VR or simulation environments?'], errors='coerce')

# Mapping for keeping ONLY the integers
q1_rename = {
    'gender_int': 'q1_gender',
    'bicycle_freq_int': 'q1_bike_freq',
    'vr_ever_int': 'q1_vr_ever',
    'vr_freq_int': 'q1_vr_freq',
    'age_int': 'q1_age',
    'cycling_confidence_int': 'q1_bike_conf',
    'vr_familiarity_int': 'q1_vr_fam'
}

# ==========================================
# 4. FINAL MERGE
# ==========================================
print("Merging data...")

# Clean IDs for merging
df_survey2['Participant ID'] = pd.to_numeric(df_survey2['Participant ID'], errors='coerce').fillna(0).astype(int)
df_survey2['Trial.No']       = pd.to_numeric(df_survey2['Trial.No'], errors='coerce').fillna(0).astype(int)
df_survey1['Participant ID'] = pd.to_numeric(df_survey1['Participant ID'], errors='coerce').fillna(0).astype(int)
df_metrics['participant_id'] = df_metrics['participant_id'].astype(int)
df_metrics['trial_order']    = df_metrics['trial_order'].astype(int)

# 4a. Prepare S2 (Scores) - Keep only IDs and named scores
s2_keep = ['Participant ID', 'Trial.No'] + list(q2_rename.keys())
df_s2_clean = df_survey2[s2_keep].rename(columns=q2_rename)

# 4b. Prepare S1 (Demographics) - Keep only ID and integer mappings
s1_keep = ['Participant ID'] + list(q1_rename.keys())
df_s1_clean = df_survey1[s1_keep].rename(columns=q1_rename)

# 4c. Merge everything
df_merged = pd.merge(df_metrics, df_s2_clean, 
                     left_on=['participant_id', 'trial_order'], 
                     right_on=['Participant ID', 'Trial.No'], how='left').drop(columns=['Participant ID', 'Trial.No'])

df_final = pd.merge(df_merged, df_s1_clean, 
                    left_on='participant_id', 
                    right_on='Participant ID', how='left').drop(columns=['Participant ID'])

# ==========================================
# 5. SAVE
# ==========================================
output_path = os.path.join(OUTPUT_DIR, MERGED_OUT)
df_final.to_csv(output_path, index=False)

print("-" * 30)
print(f"SUCCESS")
print(f"File saved to: {output_path}")