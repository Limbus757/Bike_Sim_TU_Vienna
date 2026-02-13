import pandas as pd

# Load datasets
df_demo = pd.read_csv('Official Data Collection - Form responses 1.csv')
df_trials = pd.read_csv('Official Data Collection - Form responses 2.csv')
df_metrics = pd.read_csv('logged_trial_metrics_combined.csv')

# --- TRUST CALCULATION ---
t_pos_idx = ["1", "2", "4", "6", "7", "9", "11", "12"]
t_neg_idx = ["3", "5", "8", "10"]

t_pos_cols = [c for c in df_trials.columns if any(c.startswith(n + ".") for n in t_pos_idx)]
t_neg_cols = [c for c in df_trials.columns if any(c.startswith(n + ".") for n in t_neg_idx)]

trust_calc = df_trials[t_pos_cols + t_neg_cols].copy()
for col in t_neg_cols:
    trust_calc[col] = 6 - pd.to_numeric(trust_calc[col], errors='coerce')

df_trials['Trust_Score'] = trust_calc.mean(axis=1)

# --- TAM CALCULATION ---
pu_cols = [c for c in df_trials.columns if any(c.startswith(n + ".") for n in ["13", "14", "15", "16", "17"])]
peou_cols = [c for c in df_trials.columns if any(c.startswith(n + ".") for n in ["18", "19", "20", "21", "22"])]

df_trials['TAM_PU'] = df_trials[pu_cols].apply(pd.to_numeric, errors='coerce').mean(axis=1)
df_trials['TAM_PEOU'] = df_trials[peou_cols].apply(pd.to_numeric, errors='coerce').mean(axis=1)
df_trials['TAM_Total'] = df_trials[pu_cols + peou_cols].apply(pd.to_numeric, errors='coerce').mean(axis=1)

# --- MERGING DEMOGRAPHICS ---
demo_cols = [
    'Participant ID', 'Specify what gender you identify with', 'How old are you?',
    '  How often do you ride a bicycle?  ', 'How confident are you in your cycling skills?',
    '  Have you used VR systems or simulators before?  ', 
    '  If yes, how often do you use VR or simulators?  ',
    'How familiar are you with VR or simulation environments?  '
]
df_survey_merged = pd.merge(df_trials, df_demo[demo_cols], on='Participant ID', how='left')

# --- SAVE SURVEY-ONLY METRICS CSV ---
survey_output_cols = ['Participant ID', 'Trial.No', 'Trust_Score', 'TAM_PU', 'TAM_PEOU', 'TAM_Total'] + demo_cols[1:]
df_survey_only = df_survey_merged[survey_output_cols]
df_survey_only.to_csv('survey_metrics_only.csv', index=False)

# --- MERGE WITH TRIAL PERFORMANCE METRICS ---
df_survey_merged['Participant ID'] = df_survey_merged['Participant ID'].astype(int)
df_survey_merged['Trial.No'] = df_survey_merged['Trial.No'].astype(int)
df_metrics['participant_id'] = df_metrics['participant_id'].astype(int)
df_metrics['trial_order'] = df_metrics['trial_order'].astype(int)

df_combined = pd.merge(
    df_metrics,
    df_survey_merged,
    left_on=['participant_id', 'trial_order'],
    right_on=['Participant ID', 'Trial.No'],
    how='left'
)

# --- COMMAND LINE OUTPUT ---
print(f"Total unique participants: {df_combined['participant_id'].nunique()}")
print(f"Total trials processed: {len(df_combined)}")

# Check for duplicates based on participant and trial
duplicates = df_combined[df_combined.duplicated(subset=['participant_id', 'trial_order'], keep=False)]
if not duplicates.empty:
    print("\nDUPLICATES FOUND:")
    print(duplicates[['participant_id', 'trial_order']])

# --- SAVE FINAL FILE ---
final_cols = list(df_metrics.columns) + ['Trust_Score', 'TAM_PU', 'TAM_PEOU', 'TAM_Total'] + demo_cols[1:]
df_combined[final_cols].to_csv('fully_combined_metrics.csv', index=False)