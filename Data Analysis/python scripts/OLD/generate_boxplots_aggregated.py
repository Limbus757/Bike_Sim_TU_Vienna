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
base_folder = "boxplots_aggregated" # Changed name to distinguish from trial-level
full_folder = os.path.join(base_folder, "full")
zoomed_folder = os.path.join(base_folder, "zoomed")
for folder in [base_folder, full_folder, zoomed_folder]:
    os.makedirs(folder, exist_ok=True)

# ==========================================
# 1. LOAD AGGREGATED DATA
# ==========================================
# This file contains the Pooled SDs and Mean performance per participant
df = pd.read_csv('fully_combined_aggregated.csv')

# 2. Define metrics (Ensuring names match your new combined file)
metrics = [
    # Driving Performance
    ('laptime_total_seconds', 'Total Lap Time', 's'),
    ('speed_mean_kmh', 'Driving Speed (Mean)', 'kmh'),
    ('speed_sd_kmh', 'Speed Variability (Pooled SD)', 'kmh'),
    ('cte_absmean_meters', 'Lane Keeping Error (CTE Mean)', 'm'),
    ('cte_sd_meters', 'Lane Keeping Variability (Pooled SD)', 'm'),
    ('headingerror_absmean_deg', 'Heading Error (Mean)', 'deg'),
    ('headingerror_sd_deg', 'Heading Error Variability (Pooled SD)', 'deg'),
    ('steeringangle_sd_deg', 'Steering Variability (Pooled SD)', 'deg'),
    
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
    
    # Since df is aggregated, one row = one participant
    outliers = df.loc[subset[(subset < low) | (subset > high)].index, 'participant_id'].tolist()
    
    if not outliers: return "None"
    return ", ".join(map(str, sorted(outliers)))

def generate_plot(col, title, unit):
    if col not in df.columns:
        print(f"Skipping {col}: Not found in CSV")
        return 

    all_vals = df[col].dropna()
    if all_vals.empty: return

    # Global IQR for zooming logic
    q1, q3 = all_vals.quantile(0.25), all_vals.quantile(0.75)
    iqr = q3 - q1
    zmin, zmax = max(all_vals.min(), q1 - 1.5*iqr), min(all_vals.max(), q3 + 1.5*iqr)

    for version, (ymin, ymax) in enumerate([(all_vals.min(), all_vals.max()), (zmin, zmax)]):
        plt.figure(figsize=(10, 12)) 
        
        ax = sns.boxplot(
            data=df, x='condition', y=col, order=condition_order, palette='Set2', width=0.6,
            showfliers=(version==0), showmeans=True, meanline=True,
            meanprops={'linestyle': '--', 'linewidth': 2, 'color': 'black', 'alpha': 0.6},
            medianprops={'linestyle': '-', 'linewidth': 2.5, 'color': 'black'}
        )
        
        # Add Swarmplot to see the 11 individual participants
        sns.swarmplot(data=df, x='condition', y=col, order=condition_order, color=".25", size=6, alpha=0.6)
        
        ax.set_xticks(range(len(condition_order)))
        ax.set_xticklabels(condition_order, fontsize=14, fontweight='bold')
        
        for i, cond in enumerate(condition_order):
            subset = df[df['condition'] == cond][col].dropna()
            if not subset.empty:
                n, med, mu, sd = len(subset), subset.median(), subset.mean(), subset.std()
                out_str = get_outlier_info(df, col, cond)
                
                stats_label = (f"$N$={n}\n"
                               f"$\\tilde{{x}}$={med:.2f}\n"
                               f"$\\mu$={mu:.2f}\n"
                               f"$\\sigma$={sd:.2f}\n"
                               f"Outliers: {out_str}")
                
                ax.text(i, -0.1, stats_label, 
                        transform=ax.get_xaxis_transform(),
                        ha='center', va='top', 
                        fontsize=10, 
                        linespacing=1.8)
        
        yr = max(ymax - ymin, 0.01)
        ax.set_ylim(ymin - 0.15 * yr, ymax + 0.35 * yr)
        plt.title(f"{title}{' (Zoomed)' if version==1 else ''}", fontweight='bold', fontsize=18, pad=20)
        plt.ylabel(f"Value [{unit}]", fontsize=12)
        plt.xlabel('')

        custom_lines = [
            Line2D([0], [0], color='black', lw=2, linestyle='-', label='Median'),
            Line2D([0], [0], color='black', lw=2, linestyle='--', alpha=0.6, label='Mean')
        ]
        ax.legend(handles=custom_lines, loc='upper right', frameon=True)
        plt.subplots_adjust(bottom=0.3) 
        
        path = full_folder if version == 0 else zoomed_folder
        plt.savefig(os.path.join(path, f"{col}.png"), dpi=300, bbox_inches='tight')
        plt.close()

print("Generating plots from aggregated data...")
for col, title, unit in metrics:
    generate_plot(col, title, unit)
print("Complete! Check the 'boxplots_aggregated' folder.")