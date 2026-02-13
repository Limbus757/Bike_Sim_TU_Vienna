import pandas as pd
import seaborn as sns
import matplotlib.pyplot as plt
import numpy as np
from matplotlib.lines import Line2D
import warnings
import os

# Suppress warnings
warnings.filterwarnings('ignore', category=FutureWarning)
warnings.filterwarnings('ignore', category=UserWarning)

# Folders
base_folder = "boxplots"
full_folder = os.path.join(base_folder, "full")
zoomed_folder = os.path.join(base_folder, "zoomed")
for folder in [base_folder, full_folder, zoomed_folder]:
    os.makedirs(folder, exist_ok=True)

# 1. Load data
df = pd.read_csv('logged_trial_metrics_combined.csv')

# 2. Define metrics
metrics = [
    # Driving Performance
    ('laptime_total_seconds', 'Total Lap Time', 's'),
    ('speed_mean_kmh', 'Driving Speed (Mean)', 'kmh'),
    ('speed_sd_kmh', 'Speed Variability (SD)', 'kmh'),
    ('cte_absmean_meters', 'Lane Keeping Error (CTE Mean)', 'm'),
    ('cte_sd_meters', 'Lane Keeping Variability (CTE SD)', 'm'),
    ('headingerror_absmean_deg', 'Heading Error (Mean)', 'deg'),
    ('headingerror_sd_deg', 'Heading Error Variability (SD)', 'deg'),
    ('steeringangle_sd_deg', 'Steering Variability (SD)', 'deg'),
    
    # Overall LKA & Vibration
    ('lka_switch_on_pct_overall', 'LKA Switch-On % (Overall)', '%'), # ADDED THIS
    ('lka_active_while_switched_on_pct_overall', 'LKA Activation % (Overall)', '%'),
    ('lka_effort_absmean_overall', 'LKA Effort (Overall)', 'norm'),
    ('vibration_intensity_both_overall', 'Vibration Intensity (Overall)', 'norm'),
    ('vibration_engaged_pct_overall', 'Vibration Engagement (Overall)', '%'),
    
    # Segmented Breakdown
    ('switch_pct_straight', 'LKA Switch-On % (Straights)', '%'),
    ('lka_active_while_switched_on_pct_straight', 'LKA Activation % (Straights)', '%'),
    ('lka_effort_absmean_straight', 'LKA Effort (Straights)', 'norm'),
    ('vibration_engaged_pct_straight', 'Vibration Engagement (Straights)', '%'),
    
    ('switch_pct_short_turn', 'LKA Switch-On % (Short Turns)', '%'),
    ('lka_active_while_switched_on_pct_short_turn', 'LKA Activation % (Short Turns)', '%'),
    ('lka_effort_absmean_short_turn', 'LKA Effort (Short Turns)', 'norm'),
    ('vibration_engaged_pct_short_turn', 'Vibration Engagement (Short Turns)', '%'),
    
    ('switch_pct_long_turn', 'LKA Switch-On % (Long Turns)', '%'),
    ('lka_active_while_switched_on_pct_long_turn', 'LKA Activation % (Long Turns)', '%'),
    ('lka_effort_absmean_long_turn', 'LKA Effort (Long Turns)', 'norm'),
    ('vibration_engaged_pct_long_turn', 'Vibration Engagement (Long Turns)', '%'),
    
    # Secondary Task
    ('nback_hitrate_pct', 'N-Back Hit Rate', '%'),
    ('nback_accuracy_corrected_pct', 'N-Back Corrected Accuracy', '%'),
    ('nback_reactiontime_mean_seconds', 'N-Back Reaction Time (Mean)', 's'),
    
    # Questionnaire Metrics
    ('Trust_Score', 'Trust Score', '1-5'),
    ('TAM_PU', 'TAM Perceived Usefulness', '1-7'),
    ('TAM_PEOU', 'TAM Perceived Ease of Use', '1-7'),
    ('TAM_Total', 'TAM Total Score', '1-7')
]

condition_order = ['Baseline', 'HapticsFixed', 'HapticsAdaptive']
sns.set_theme(style="whitegrid")

def get_outlier_info(df, col, condition):
    subset = df[df['condition'] == condition][col].dropna()
    if subset.empty: return ""
    q1, q3 = subset.quantile(0.25), subset.quantile(0.75)
    iqr = q3 - q1
    low, high = q1 - 1.5 * iqr, q3 + 1.5 * iqr
    outliers = df.loc[subset[(subset < low) | (subset > high)].index, 'participant_id'].unique().tolist()
    if not outliers: return "None"
    return ", ".join(map(str, sorted(outliers)))

def generate_plot(col, title, unit):
    if col not in df.columns:
        return 

    all_vals = df[col].dropna()
    if all_vals.empty: return

    q1, q3 = all_vals.quantile(0.25), all_vals.quantile(0.75)
    iqr = q3 - q1
    zmin, zmax = max(all_vals.min(), q1 - 1.5*iqr), min(all_vals.max(), q3 + 1.5*iqr)

    for version, (ymin, ymax) in enumerate([(all_vals.min(), all_vals.max()), (zmin, zmax)]):
        plt.figure(figsize=(12, 14)) 
        
        ax = sns.boxplot(
            data=df, x='condition', y=col, order=condition_order, palette='Set2', width=0.7,
            showfliers=(version==0), showmeans=True, meanline=True,
            meanprops={'linestyle': '--', 'linewidth': 2.5, 'color': 'black', 'alpha': 0.4},
            medianprops={'linestyle': '-', 'linewidth': 2.5, 'color': 'black'}
        )
        
        ax.set_xticks(range(len(condition_order)))
        ax.set_xticklabels(condition_order, fontsize=16, fontweight='bold')
        
        for i, cond in enumerate(condition_order):
            subset = df[df['condition'] == cond][col].dropna()
            if not subset.empty:
                n, med, mu, sd, sk = len(subset), subset.median(), subset.mean(), subset.std(), subset.skew()
                out_str = get_outlier_info(df, col, cond)
                
                stats_label = (f"$N$={n}\n"
                               f"$\\tilde{{x}}$={med:.2f}\n"
                               f"$\\mu$={mu:.2f}\n"
                               f"$\\sigma$={sd:.2f}\n"
                               f"$Sk$={sk:.2f}\n\n"
                               f"Outliers: {out_str}")
                
                ax.text(i, -0.06, stats_label, 
                        transform=ax.get_xaxis_transform(),
                        ha='center', va='top', 
                        fontsize=10, 
                        linespacing=2.0)
        
        yr = max(ymax - ymin, 0.01)
        ax.set_ylim(ymin - 0.1 * yr, ymax + 0.3 * yr)
        plt.title(f"{title}{' (Zoomed)' if version==1 else ''}", fontweight='bold', fontsize=20, pad=20)
        plt.ylabel(f"Value [{unit}]", fontsize=14)
        plt.xlabel('')

        custom_lines = [
            Line2D([0], [0], color='black', lw=2.5, linestyle='-', label='Median'),
            Line2D([0], [0], color='black', lw=2.5, linestyle='--', alpha=0.4, label='Mean')
        ]
        ax.legend(handles=custom_lines, loc='upper right', frameon=True, fontsize=12)
        plt.subplots_adjust(bottom=0.35) 
        
        path = full_folder if version == 0 else zoomed_folder
        plt.savefig(os.path.join(path, f"{col}.png"), dpi=300, bbox_inches='tight')
        plt.close()

print("Generating plots...")
for col, title, unit in metrics:
    generate_plot(col, title, unit)
print("Complete!")