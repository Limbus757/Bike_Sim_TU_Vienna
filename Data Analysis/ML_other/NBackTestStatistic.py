import pandas as pd
import matplotlib.pyplot as plt
import numpy as np
import os
from matplotlib.lines import Line2D
from matplotlib.patches import Patch

"""
Driving Simulator Analysis Pipeline - Thesis Edition
Consolidated Script: Boxplot (Median) + Reference Line (Baseline Mean)
"""

# --- 1. CONFIGURATION ---
SORT_ORDER = ['Baseline', 'HapticsFixed', 'HapticsAdaptive']
COLORS = ['#D3D3D3', '#b2d1b5', 'b2bed1'] 
WINDOW_SECONDS = 1.5

def analyze_secondary_task(group):
    group = group.sort_values('Timestamp')
    ts, is_target, pressed = group['Timestamp'].values, group['SecTaskIsTarget'].values, group['SecTaskPressed'].values
    windows = [(ts[i], ts[i] + WINDOW_SECONDS) for i in range(len(group)) 
               if is_target[i] == 1 and (i == 0 or is_target[i-1] == 0)]
    press_times = [ts[i] for i in range(len(group)) 
                   if pressed[i] == 1 and (i == 0 or pressed[i-1] == 0)]
    rts, used_press = [], [False] * len(press_times)
    for start, end in windows:
        for pt_idx, pt in enumerate(press_times):
            if not used_press[pt_idx] and start <= pt <= end:
                used_press[pt_idx] = True
                rts.append(pt - start)
                break
    return pd.Series({'Accuracy': (len(rts) / len(windows)) * 100 if windows else np.nan, 
                      'AvgRT': np.mean(rts) if rts else np.nan})

# --- 2. DATA LOADING & PROCESSING ---
files = {
    'Baseline': 'condition_Baseline.csv',
    'HapticsAdaptive': 'condition_HapticsAdaptive.csv',
    'HapticsFixed': 'condition_HapticsFixed.csv'
}

all_trial_data = []
for condition, filename in files.items():
    if os.path.exists(filename):
        df = pd.read_csv(filename, sep=';', decimal=',')
        df['abs_CTE_Meters'] = df['CTE_Meters'].abs()
        df['abs_HeadingError'] = df['B_HeadingError_Deg'].abs()
        df['abs_LkaEffort'] = df['LkaNormEffort'].abs()

        trial_agg = df.groupby(['participant_id', 'trial_order']).agg({
            'Speed_KmH': 'mean', 'Timestamp': 'max',
            'abs_CTE_Meters': ['mean', 'std'], 'abs_HeadingError': 'mean', 'abs_LkaEffort': 'mean'
        }).reset_index()
        trial_agg.columns = ['participant_id', 'trial_order', 'Avg_Speed', 'Laptime', 
                             'Mean_CTE', 'SDLP', 'Mean_HeadingErr', 'LkaEffort']
        sectask = df.groupby(['participant_id', 'trial_order']).apply(analyze_secondary_task).reset_index()
        combined = pd.merge(trial_agg, sectask, on=['participant_id', 'trial_order'])
        combined['Condition'] = condition
        all_trial_data.append(combined)

final_df = pd.concat(all_trial_data, ignore_index=True)
final_df['Condition'] = pd.Categorical(final_df['Condition'], categories=SORT_ORDER, ordered=True)
final_df = final_df.sort_values('Condition')

# --- 3. VISUALIZATION ---
row1_metrics = [('Laptime', 'Total Laptime (s)'), ('Mean_CTE', 'Lane Error (m)'), 
                ('SDLP', 'Steering Var (SDLP)'), ('Mean_HeadingErr', 'Heading Error (°)'), 
                ('Avg_Speed', 'Avg Speed (km/h)')]
row2_metrics = [('AvgRT', 'Reaction Time (s)'), ('Accuracy', 'Accuracy (%)'),
                ('LkaEffort', 'LKA Effort (Abs)'), ('None', ''), ('None', '')]
all_metrics = [row1_metrics, row2_metrics]

fig, axes = plt.subplots(2, 5, figsize=(30, 22))
actual_labels = [l for l in SORT_ORDER if l in final_df['Condition'].unique()]

for row_idx, metrics in enumerate(all_metrics):
    for col_idx, (col, title) in enumerate(metrics):
        ax = axes[row_idx, col_idx]
        if col == 'None':
            ax.axis('off')
            continue
        
        data_list = [final_df[final_df['Condition'] == c][col].dropna() for c in actual_labels]
        
        # BOXPLOT (Shows Median + Quartiles)
        bplot = ax.boxplot(data_list, labels=actual_labels, patch_artist=True, widths=0.7, 
                           medianprops=dict(color='red', linewidth=3))
        
        for patch, color in zip(bplot['boxes'], COLORS):
            patch.set_facecolor(color)
            patch.set_edgecolor('#333333')
        
        for j, dist in enumerate(data_list):
            # Jitter Points (Show individual participants)
            x = np.random.normal(j + 1, 0.03, size=len(dist))
            ax.scatter(x, dist, alpha=0.4, s=30, color='black', edgecolors='white', zorder=3)
            
            # STATS TEXT (Calculates Mean & CV)
            m, s = np.mean(dist), np.std(dist)
            cv = (s / m * 100) if m != 0 else 0
            ax.text(j + 1, -0.12, f'M: {m:.2f}\nSD: {s:.2f}\nCV: {cv:.1f}%', 
                    transform=ax.get_xaxis_transform(), ha='center', va='top', 
                    fontsize=11, fontweight='bold', bbox=dict(facecolor='white', alpha=0.8, edgecolor='none'))

        # REFERENCE LINE (The Baseline Mean)
        if 'Baseline' in actual_labels:
            base_mean = final_df[final_df['Condition'] == 'Baseline'][col].mean()
            ax.axhline(base_mean, color='red', linestyle='--', linewidth=2, alpha=0.5)

        if col in ['Mean_CTE', 'SDLP', 'Mean_HeadingErr', 'LkaEffort']: ax.set_ylim(bottom=0)
        if col == 'Accuracy': ax.set_ylim(0, 105)
        ax.set_title(title, fontweight='bold', fontsize=17, pad=20)
        ax.grid(axis='y', linestyle=':', alpha=0.6)

# --- 4. LEGEND & POLISHING ---
legend_elements = [
    Line2D([0], [0], color='red', lw=3, label='Condition Median (Typical Performance)'),
    Line2D([0], [0], color='red', lw=2, ls='--', alpha=0.6, label='Baseline Mean (Global Benchmark)'),
    Patch(facecolor=COLORS[0], label='Baseline'),
    Patch(facecolor=COLORS[1], label='Haptics Fixed'),
    Patch(facecolor=COLORS[2], label='Haptics Adaptive')
]

fig.legend(handles=legend_elements, loc='upper center', bbox_to_anchor=(0.5, 0.945),
           ncol=3, fontsize=14, frameon=True, facecolor='white', edgecolor='black')

plt.suptitle('Haptic Intervention Study: Performance Distribution and Reliability', 
             fontsize=28, fontweight='bold', y=0.985)

plt.subplots_adjust(left=0.05, bottom=0.15, hspace=0.35, wspace=0.15)
plt.savefig('haptics_thesis_final_plot.png', dpi=300, bbox_inches='tight')
plt.show()