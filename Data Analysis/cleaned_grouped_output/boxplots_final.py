import pandas as pd
import matplotlib.pyplot as plt
import numpy as np

# Configuration
WINDOW_SECONDS = 1.5

def analyze_secondary_task(group):
    """
    Calculates Accuracy and Avg Reaction Time for a single trial.
    Identifies target onsets and press events using rising-edge detection.
    """
    group = group.sort_values('Timestamp')
    ts = group['Timestamp'].values
    is_target = group['SecTaskIsTarget'].values
    pressed = group['SecTaskPressed'].values
    
    # Identify Target Windows (Rising edge of SecTaskIsTarget)
    windows = []
    for i in range(len(group)):
        curr_t = is_target[i] == 1
        prev_t = (is_target[i-1] == 1) if i > 0 else False
        if curr_t and not prev_t:
            windows.append((ts[i], ts[i] + WINDOW_SECONDS))

    # Identify Press Events (Rising edge of SecTaskPressed)
    press_times = []
    for i in range(len(group)):
        curr_p = pressed[i] == 1
        prev_p = (pressed[i-1] == 1) if i > 0 else False
        if curr_p and not prev_p:
            press_times.append(ts[i])
            
    # Match first unused press to each window
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

# Calculate absolute values for core metrics
df['abs_CTE_Meters'] = df['CTE_Meters'].abs()
df['abs_B_HeadingError_Deg'] = df['B_HeadingError_Deg'].abs()
df['abs_LkaNormEffort'] = df['LkaNormEffort'].abs()

# Aggregate Metrics
trial_groups = df.groupby(['participant_id', 'trial_condition', 'trial_order'])

# Lap Completion Times
trial_durations = trial_groups['Timestamp'].agg(lambda x: x.max() - x.min()).reset_index()
trial_durations.rename(columns={'Timestamp': 'LapTime'}, inplace=True)

# Secondary Task Metrics
sectask_metrics = trial_groups.apply(analyze_secondary_task).reset_index()

# Merge all trial-level results
trial_results = pd.merge(trial_durations, sectask_metrics, on=['participant_id', 'trial_condition', 'trial_order'])

# Calculate Thresholds (Participant Level)
# Calculate the mean for every participant first, then take the percentiles of those means
p_driving_means = df.groupby('participant_id').agg({
    'abs_CTE_Meters': 'mean',
    'abs_B_HeadingError_Deg': 'mean',
    'abs_LkaNormEffort': 'mean'
})
p_trial_means = trial_results.groupby('participant_id').agg({
    'AvgRT': 'mean',
    'Accuracy': 'mean'
})
all_p_means = pd.concat([p_driving_means, p_trial_means], axis=1)

# Define thresholds
# 75th Percentile = Poor performance for errors/time (High is bad)
# 25th Percentile = Poor performance for accuracy (Low is bad)
thresholds = {
    'abs_CTE_Meters': all_p_means['abs_CTE_Meters'].quantile(0.75),
    'abs_B_HeadingError_Deg': all_p_means['abs_B_HeadingError_Deg'].quantile(0.75),
    'abs_LkaNormEffort': all_p_means['abs_LkaNormEffort'].quantile(0.75),
    'AvgRT': all_p_means['AvgRT'].quantile(0.75),
    'Accuracy': all_p_means['Accuracy'].quantile(0.25)
}

# Identify Threshold Crossers
trial_driving_stats = df.groupby(['participant_id', 'trial_condition', 'trial_order']).agg({
    'abs_CTE_Meters': ['mean', 'max'],
    'abs_B_HeadingError_Deg': ['mean', 'max'],
    'abs_LkaNormEffort': ['mean', 'max']
})

report_categories = ['abs_CTE_Meters', 'abs_B_HeadingError_Deg', 'abs_LkaNormEffort', 'AvgRT', 'Accuracy']
columns_data = {}

for col in report_categories:
    thresh = thresholds[col]
    label = "Thresh 25th" if col == 'Accuracy' else "Thresh 75th"
    text = f"{col}\n({label}: {thresh:.4f})\n" + "-"*35 + "\n"
    
    if col == 'Accuracy':
        # Find trials where Accuracy < 25th percentile
        target_ps = all_p_means[all_p_means[col] < thresh].index.tolist()
        for p_id in target_ps:
            p_trials = trial_results[trial_results['participant_id'] == p_id]
            matching = p_trials[p_trials[col] < thresh]
            for _, row in matching.iterrows():
                text += f"P{p_id} | {row['trial_condition']:14} | Val: {row[col]:.3f}\n"
    elif col in ['AvgRT']:
        # Find trials where AvgRT > 75th percentile
        target_ps = all_p_means[all_p_means[col] > thresh].index.tolist()
        for p_id in target_ps:
            p_trials = trial_results[trial_results['participant_id'] == p_id]
            matching = p_trials[p_trials[col] > thresh]
            for _, row in matching.iterrows():
                text += f"P{p_id} | {row['trial_condition']:14} | Val: {row[col]:.3f}\n"
    else:
        # Driving metrics (CTE, Heading, Effort)
        target_ps = all_p_means[all_p_means[col] > thresh].index.tolist()
        for p_id in target_ps:
            p_trials = trial_driving_stats.xs(p_id, level='participant_id')
            matching = p_trials[p_trials[(col, 'mean')] > thresh]
            for (cond, order), row in matching.iterrows():
                text += f"P{p_id} | {cond:14} | Max: {row[(col, 'max')]:.3f}\n"
    
    columns_data[col] = text

# Visualization
plot_configs = [
    {'data': df['abs_CTE_Meters'], 'title': 'Abs CTE (M)', 'ylabel': 'Meters', 't_key': 'abs_CTE_Meters'},
    {'data': df['abs_B_HeadingError_Deg'], 'title': 'Abs Heading Err', 'ylabel': 'Degrees', 't_key': 'abs_B_HeadingError_Deg'},
    {'data': df['abs_LkaNormEffort'], 'title': 'Abs LKA Effort', 'ylabel': 'Norm', 't_key': 'abs_LkaNormEffort'},
    {'data': trial_results['AvgRT'].dropna(), 'title': 'SecTask Avg RT', 'ylabel': 'Seconds', 't_key': 'AvgRT'},
    {'data': trial_results['Accuracy'].dropna(), 'title': 'SecTask Accuracy', 'ylabel': 'Ratio', 't_key': 'Accuracy'},
    {'data': trial_results['LapTime'], 'title': 'Lap Time', 'ylabel': 'Seconds', 't_key': None}
]

fig, axes = plt.subplots(2, 6, figsize=(32, 25))
plt.subplots_adjust(bottom=0.35, hspace=0.3)

for row_idx, show_fliers in enumerate([True, False]):
    for col_idx, config in enumerate(plot_configs):
        ax = axes[row_idx, col_idx]
        data = config['data']
        ax.boxplot(data, showfliers=show_fliers)
        
        label_type = " (Full)" if show_fliers else " (Zoom)"
        ax.set_title(config['title'] + label_type, fontweight='bold', fontsize=14)
        ax.set_ylabel(config['ylabel'], fontsize=12)
        
        # Mean/SD Box
        stats_lbl = f"Mean: {data.mean():.2f}\nSD: {data.std():.2f}"
        ax.text(0.95, 0.95, stats_lbl, transform=ax.transAxes, va='top', ha='right', 
                bbox=dict(facecolor='white', alpha=0.8), fontsize=11)
        
        # Draw Threshold Lines
        tk = config['t_key']
        if tk in thresholds:
            color = 'blue' if tk == 'Accuracy' else 'red'
            ax.axhline(thresholds[tk], color=color, linestyle='--', alpha=0.6)

# Add Summary Text at Bottom
fig.text(0.05, 0.32, "CROSSING DETAILS (POOR PERFORMANCE SAMPLES):", fontsize=16, fontweight='bold')
x_positions = np.linspace(0.05, 0.95, len(report_categories) + 1)[:-1]
for i, col_name in enumerate(report_categories):
    fig.text(x_positions[i], 0.30, columns_data[col_name], fontsize=10, fontfamily='monospace', va='top', linespacing=1.3)

plt.savefig('final_integrated_analysis.png', bbox_inches='tight', dpi=300)
plt.show()