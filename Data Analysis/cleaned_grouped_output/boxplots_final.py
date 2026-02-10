import pandas as pd
import matplotlib.pyplot as plt
import numpy as np
from collections import defaultdict

# Configuration
WINDOW_SECONDS = 1.5

def analyze_secondary_task(group):
    group = group.sort_values('Timestamp')
    ts = group['Timestamp'].values
    is_target = group['SecTaskIsTarget'].values
    pressed = group['SecTaskPressed'].values
    
    windows = []
    for i in range(len(group)):
        curr_t = is_target[i] == 1
        prev_t = (is_target[i-1] == 1) if i > 0 else False
        if curr_t and not prev_t:
            windows.append((ts[i], ts[i] + WINDOW_SECONDS))

    press_times = []
    for i in range(len(group)):
        curr_p = pressed[i] == 1
        prev_p = (pressed[i-1] == 1) if i > 0 else False
        if curr_p and not prev_p:
            press_times.append(ts[i])
            
    used_press = [False] * len(press_times)
    rts = []
    for start, end in windows:
        for j, pt in enumerate(press_times):
            if not used_press[j] and start <= pt <= end:
                used_press[j] = True
                rts.append(pt - start)
                break
                
    accuracy = len(rts) / len(windows) if len(windows) > 0 else np.nan
    avg_rt = np.mean(rts) if len(rts) > 0 else np.nan
    return pd.Series({'Accuracy': accuracy, 'AvgRT': avg_rt})

# Load data
df = pd.read_csv('everything_combined.csv', sep=';', decimal=',')

# Calculate absolute values
df['abs_CTE_Meters'] = df['CTE_Meters'].abs()
df['abs_B_HeadingError_Deg'] = df['B_HeadingError_Deg'].abs()
df['abs_LkaNormEffort'] = df['LkaNormEffort'].abs()

trial_groups = df.groupby(['participant_id', 'trial_condition', 'trial_order'])
trial_durations = trial_groups['Timestamp'].agg(lambda x: x.max() - x.min()).reset_index()
trial_durations.rename(columns={'Timestamp': 'LapTime'}, inplace=True)
sectask_metrics = trial_groups.apply(analyze_secondary_task).reset_index()
trial_results = pd.merge(trial_durations, sectask_metrics, on=['participant_id', 'trial_condition', 'trial_order'])

# --- CALCULATE THRESHOLDS ---
p_driving_means = df.groupby('participant_id').agg({
    'abs_CTE_Meters': 'mean',
    'abs_B_HeadingError_Deg': 'mean',
    'abs_LkaNormEffort': 'mean'
})
p_trial_means = trial_results.groupby('participant_id').agg({
    'AvgRT': 'mean',
    'Accuracy': 'mean',
    'LapTime': 'mean'
})
all_p_means = pd.concat([p_driving_means, p_trial_means], axis=1)

thresholds = {
    'abs_CTE_Meters': all_p_means['abs_CTE_Meters'].quantile(0.75),
    'abs_B_HeadingError_Deg': all_p_means['abs_B_HeadingError_Deg'].quantile(0.75),
    'abs_LkaNormEffort': all_p_means['abs_LkaNormEffort'].quantile(0.75),
    'AvgRT': all_p_means['AvgRT'].quantile(0.75),
    'Accuracy': all_p_means['Accuracy'].quantile(0.25),
    'LapTime': all_p_means['LapTime'].quantile(0.75)
}

# --- IDENTIFY THRESHOLD CROSSERS & TRACK FLAGS ---
trial_driving_stats = df.groupby(['participant_id', 'trial_condition', 'trial_order']).agg({
    'abs_CTE_Meters': ['mean', 'max'],
    'abs_B_HeadingError_Deg': ['mean', 'max'],
    'abs_LkaNormEffort': ['mean', 'max']
})

report_categories = ['abs_CTE_Meters', 'abs_B_HeadingError_Deg', 'abs_LkaNormEffort', 'AvgRT', 'Accuracy', 'LapTime']
columns_data = {}
# Dictionary to store which categories each participant failed in
p_flags = defaultdict(set)

for col in report_categories:
    thresh = thresholds[col]
    label = "Thresh 25th" if col == 'Accuracy' else "Thresh 75th"
    text = f"{col}\n({label}: {thresh:.2f})\n" + "-"*32 + "\n"
    
    # Logic to identify crossers and update p_flags
    if col == 'Accuracy':
        target_ps = all_p_means[all_p_means[col] < thresh].index.tolist()
        for p_id in target_ps:
            p_flags[p_id].add(col) # Mark participant as poor in this category
            p_trials = trial_results[trial_results['participant_id'] == p_id]
            matching = p_trials[p_trials[col] < thresh]
            for _, row in matching.iterrows():
                text += f"P{p_id} | {row['trial_condition']:12} | Val: {row[col]:.3f}\n"
    elif col in ['AvgRT', 'LapTime']:
        target_ps = all_p_means[all_p_means[col] > thresh].index.tolist()
        for p_id in target_ps:
            p_flags[p_id].add(col)
            p_trials = trial_results[trial_results['participant_id'] == p_id]
            matching = p_trials[p_trials[col] > thresh]
            for _, row in matching.iterrows():
                text += f"P{p_id} | {row['trial_condition']:12} | Val: {row[col]:.2f}\n"
    else:
        target_ps = all_p_means[all_p_means[col] > thresh].index.tolist()
        for p_id in target_ps:
            p_flags[p_id].add(col)
            p_trials = trial_driving_stats.xs(p_id, level='participant_id')
            matching = p_trials[p_trials[(col, 'mean')] > thresh]
            for (cond, order), row in matching.iterrows():
                text += f"P{p_id} | {cond:12} | Max: {row[(col, 'max')]:.3f}\n"
    columns_data[col] = text

# Create Multi-Category Summary Text
summary_text = "PARTICIPANTS WITH POOR PERFORMANCE IN 3+ CATEGORIES:\n" + "="*60 + "\n"
found_multi = False
for p_id, failed_cols in sorted(p_flags.items()):
    if len(failed_cols) >= 3:
        found_multi = True
        summary_text += f"Participant {p_id}: Failed {len(failed_cols)} categories ({', '.join(failed_cols)})\n"
if not found_multi:
    summary_text += "No participants exceeded 3+ poor performance thresholds."

# --- VISUALIZATION ---
plot_configs = [
    {'data': df['abs_CTE_Meters'], 'title': 'Abs CTE (M)', 'ylabel': 'Meters', 't_key': 'abs_CTE_Meters'},
    {'data': df['abs_B_HeadingError_Deg'], 'title': 'Abs Heading Err', 'ylabel': 'Degrees', 't_key': 'abs_B_HeadingError_Deg'},
    {'data': df['abs_LkaNormEffort'], 'title': 'Abs LKA Effort', 'ylabel': 'Norm', 't_key': 'abs_LkaNormEffort'},
    {'data': trial_results['AvgRT'].dropna(), 'title': 'SecTask Avg RT', 'ylabel': 'Seconds', 't_key': 'AvgRT'},
    {'data': trial_results['Accuracy'].dropna(), 'title': 'SecTask Accuracy', 'ylabel': 'Ratio', 't_key': 'Accuracy'},
    {'data': trial_results['LapTime'], 'title': 'Lap Time', 'ylabel': 'Seconds', 't_key': 'LapTime'}
]

# Using height_ratios ensures the boxes stretch to fill the vertical space
fig, axes = plt.subplots(2, 6, figsize=(34, 25), gridspec_kw={'height_ratios': [1, 1]})

# Adjusting hspace (vertical gap between plots) and bottom (room for text)
# Decreasing bottom from 0.42 to 0.30 makes the plots taller
plt.subplots_adjust(top=0.95, bottom=0.30, hspace=0.25, wspace=0.3) 

for row_idx, show_fliers in enumerate([True, False]):
    for col_idx, config in enumerate(plot_configs):
        ax = axes[row_idx, col_idx]
        data = config['data']
        ax.boxplot(data, showfliers=show_fliers)
        ax.set_title(config['title'] + (" (Full)" if show_fliers else " (Zoom)"), fontweight='bold', fontsize=14)
        ax.set_ylabel(config['ylabel'], fontsize=12)
        
        stats_lbl = f"Mean: {data.mean():.2f}\nSD: {data.std():.2f}"
        ax.text(0.95, 0.95, stats_lbl, transform=ax.transAxes, va='top', ha='right', bbox=dict(facecolor='white', alpha=0.8), fontsize=10)
        
        tk = config['t_key']
        if tk in thresholds:
            color = 'blue' if tk == 'Accuracy' else 'red'
            ax.axhline(thresholds[tk], color=color, linestyle='--', alpha=0.6)

# --- ADD TEXT AT BOTTOM (Coordinates adjusted for new plot height) ---
# We move the text y-coordinates down slightly since the plots are now taller
fig.text(0.13, 0.27, "Outliers (Poor Performance):", fontsize=16, fontweight='bold')

x_positions = np.linspace(0.13, 0.82, len(report_categories))
for i, col_name in enumerate(report_categories):
    fig.text(x_positions[i], 0.25, columns_data[col_name], fontsize=9, fontfamily='monospace', va='top', linespacing=1.3)

# Summary Block
fig.text(0.13, 0.08, summary_text, fontsize=12, fontfamily='monospace', fontweight='bold', 
         va='top', bbox=dict(facecolor='red', alpha=0.1, edgecolor='red'))

plt.savefig('final_integrated_analysis.png', bbox_inches='tight', dpi=300)
plt.show()