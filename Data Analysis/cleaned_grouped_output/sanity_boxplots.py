import pandas as pd
import matplotlib.pyplot as plt
import numpy as np

# Load data
df = pd.read_csv('everything_combined.csv', sep=';', decimal=',')

# Calculate absolute values for core metrics
df['abs_CTE_Meters'] = df['CTE_Meters'].abs()
df['abs_CTE_Norm'] = df['CTE_Norm'].abs()
df['abs_B_HeadingError_Deg'] = df['B_HeadingError_Deg'].abs()
df['abs_LkaNormEffort'] = df['LkaNormEffort'].abs()

# Calculate lap completion times per trial
trial_durations = df.groupby(['participant_id', 'trial_condition', 'trial_order'])['Timestamp'].agg(lambda x: x.max() - x.min()).reset_index()
trial_durations.rename(columns={'Timestamp': 'LapTime'}, inplace=True)

# Calculate thresholds (75th percentile of participant means)
participant_means = df.groupby('participant_id').agg({
    'abs_CTE_Meters': 'mean',
    'abs_B_HeadingError_Deg': 'mean',
    'abs_LkaNormEffort': 'mean'
})
thresholds = participant_means.quantile(0.75)

# Identify trials that cross the calculated thresholds
trial_stats = df.groupby(['participant_id', 'trial_condition', 'trial_order']).agg({
    'abs_CTE_Meters': ['mean', 'max'],
    'abs_B_HeadingError_Deg': ['mean', 'max'],
    'abs_LkaNormEffort': ['mean', 'max']
})

# Construct text strings for the three columns at the bottom
columns_data = {}
categories = ['abs_CTE_Meters', 'abs_B_HeadingError_Deg', 'abs_LkaNormEffort']
for col in categories:
    thresh = thresholds[col]
    target_ps = participant_means[participant_means[col] > thresh].index.tolist()
    text = f"{col}\n(Thresh: {thresh:.4f})\n" + "-"*35 + "\n"
    for p_id in target_ps:
        p_trials = trial_stats.xs(p_id, level='participant_id')
        matching = p_trials[p_trials[(col, 'mean')] > thresh]
        for (cond, order), row in matching.iterrows():
            text += f"P{p_id} | {cond:14} | Max: {row[(col, 'max')]:.3f}\n"
    columns_data[col] = text

# Configure subplots (Full view and Zoomed view)
plot_configs = [
    {'data': df['Speed_KmH'], 'title': 'Speed (KmH)', 'ylabel': 'Km/H', 't_key': None},
    {'data': df['abs_CTE_Meters'], 'title': 'Abs CTE (M)', 'ylabel': 'Meters', 't_key': 'abs_CTE_Meters'},
    {'data': df['abs_CTE_Norm'], 'title': 'Abs CTE (Norm)', 'ylabel': 'Norm', 't_key': None},
    {'data': df['abs_B_HeadingError_Deg'], 'title': 'Abs Heading Err', 'ylabel': 'Degrees', 't_key': 'abs_B_HeadingError_Deg'},
    {'data': df['abs_LkaNormEffort'], 'title': 'Abs LKA Effort', 'ylabel': 'Norm', 't_key': 'abs_LkaNormEffort'},
    {'data': trial_durations['LapTime'], 'title': 'Lap Time', 'ylabel': 'Seconds', 't_key': None}
]

# Generate the figure
fig, axes = plt.subplots(2, 6, figsize=(28, 25))
plt.subplots_adjust(bottom=0.35, hspace=0.3)

for row_idx, show_fliers in enumerate([True, False]):
    for col_idx, config in enumerate(plot_configs):
        ax = axes[row_idx, col_idx]
        data = config['data'].dropna()
        ax.boxplot(data, showfliers=show_fliers)
        
        label_type = " (Full)" if show_fliers else " (Zoom)"
        ax.set_title(config['title'] + label_type, fontweight='bold', fontsize=14)
        ax.set_ylabel(config['ylabel'], fontsize=12)
        
        # Annotate Mean and SD
        stats_lbl = f"Mean: {data.mean():.2f}\nSD: {data.std():.2f}"
        ax.text(0.95, 0.95, stats_lbl, transform=ax.transAxes, va='top', ha='right', 
                bbox=dict(facecolor='white', alpha=0.8), fontsize=11)
        
        # Add a reference line for the threshold
        t_key = config['t_key']
        if t_key and t_key in thresholds:
            ax.axhline(thresholds[t_key], color='red', linestyle='--', alpha=0.5)

# Place the category text columns in the reserved bottom area
fig.text(0.05, 0.32, "CROSSING DETAILS BY CATEGORY:", fontsize=16, fontweight='bold')
x_positions = [0.05, 0.37, 0.69]
for i, col_name in enumerate(categories):
    fig.text(x_positions[i], 0.30, columns_data[col_name], fontsize=12, fontfamily='monospace', va='top', linespacing=1.3)

# Save high-resolution final image
plt.savefig('clean_analysis.png', bbox_inches='tight', dpi=300)
plt.show()