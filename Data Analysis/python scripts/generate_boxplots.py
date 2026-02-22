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
# Using the aggregated file we just created
df = pd.read_csv('fully_combined_metrics_aggregated.csv')

# 2. Define metrics
# Global metrics (3 boxes)
global_metrics = [
    ('laptime_total_seconds', 'Total Lap Time', 's'),
    ('speed_mean_kmh', 'Driving Speed (Mean)', 'kmh'),
    ('lka_switch_on_pct', 'LKA Switch-On %', '%'),
    ('nback_accuracy_corrected_pct', 'N-Back Corrected Accuracy', '%'),
    ('Trust_Score', 'Trust Score', '1-5'),
    ('TAM_Total', 'TAM Total Score', '1-7')
]

# Segmented metric bases (will produce 9-box plots)
segmented_bases = [
    ('cte_absmean', 'Lane Keeping Error (CTE Mean)', 'm'),
    ('cte_sd', 'Lane Keeping Variability (CTE SD)', 'm'),
    ('headingerror_absmean', 'Heading Error (Mean)', 'deg'),
    ('headingerror_sd', 'Heading Error Variability (SD)', 'deg'),
    ('lka_intervention_rate', 'Intervention Rate', 'rate'),
    ('vibration_intensity', 'Vibration Intensity', 'norm'),
    ('lka_active_effort', 'LKA Active Effort', 'norm'),
    ('vibration_engaged_pct', 'Vibration Engagement', '%')
]

condition_order = ['Baseline', 'HapticsFixed', 'HapticsAdaptive']
segment_order = ['straight', 'short_turn', 'long_turn']
segment_labels = ['Straight', 'Short Turn', 'Long Turn']

CONDITION_COLORS = {
    'Baseline': '#D3D3D3',
    'HapticsFixed': '#c1d4be',
    'HapticsAdaptive': '#bec1d4'
}

sns.set_theme(style="whitegrid")

def get_outlier_info(data, col, condition, segment=None):
    subset_mask = (data['condition'] == condition)
    if segment:
        subset_mask &= (data['segment'] == segment)
    
    subset = data.loc[subset_mask, col].dropna()
    if subset.empty: return "None"
    
    q1, q3 = subset.quantile(0.25), subset.quantile(0.75)
    iqr = q3 - q1
    low, high = q1 - 1.5 * iqr, q3 + 1.5 * iqr
    outliers = data.loc[subset[(subset < low) | (subset > high)].index, 'participant_id'].unique().tolist()
    
    if not outliers: return "None"
    return ", ".join(map(str, sorted(outliers)))

def generate_global_plot(col, title, unit):
    if col not in df.columns: return 
    all_vals = df[col].dropna()
    if all_vals.empty: return

    q1, q3 = all_vals.quantile(0.25), all_vals.quantile(0.75)
    iqr = q3 - q1
    zmin, zmax = max(all_vals.min(), q1 - 1.5*iqr), min(all_vals.max(), q3 + 1.5*iqr)

    for version, (ymin, ymax) in enumerate([(all_vals.min(), all_vals.max()), (zmin, zmax)]):
        plt.figure(figsize=(10, 12)) 
        ax = sns.boxplot(
            data=df, x='condition', y=col, order=condition_order, palette=CONDITION_COLORS, width=0.6,
            showfliers=(version==0), showmeans=True, meanline=True,
            meanprops={'linestyle': '--', 'linewidth': 2.0, 'color': 'black', 'alpha': 0.6},
            medianprops={'linestyle': '-', 'linewidth': 2.0, 'color': 'black'}
        )
        
        # Add jittered points
        for i, cond in enumerate(condition_order):
            vals = df[df['condition'] == cond][col].dropna()
            x = np.random.normal(i, 0.04, size=len(vals))
            plt.scatter(x, vals, alpha=0.4, s=20, color='black', edgecolors='white', zorder=3)
            
            # Stats text
            n, med, mu, sd = len(vals), vals.median(), vals.mean(), vals.std()
            out_str = get_outlier_info(df, col, cond)
            stats_label = (f"N={n}\n"
                           f"Med={med:.2f}\n"
                           f"Mean={mu:.2f}\n"
                           f"SD={sd:.2f}\n"
                           f"Out: {out_str}")
            ax.text(i, -0.06, stats_label, transform=ax.get_xaxis_transform(),
                    ha='center', va='top', fontsize=9, linespacing=1.5)

        yr = max(ymax - ymin, 0.01)
        ax.set_ylim(ymin - 0.1 * yr, ymax + 0.3 * yr)
        plt.title(f"{title}{' (Zoomed)' if version==1 else ''}", fontweight='bold', fontsize=18)
        plt.ylabel(f"[{unit}]", fontsize=12)
        plt.xlabel('')
        plt.subplots_adjust(bottom=0.3)
        
        path = full_folder if version == 0 else zoomed_folder
        plt.savefig(os.path.join(path, f"global_{col}.png"), dpi=200, bbox_inches='tight')
        plt.close()

def generate_segmented_plot(base_col, title, unit):
    # Melt data for 9-box plot
    cols_to_melt = [f"{base_col}_{s}" for s in segment_order]
    if not all(c in df.columns for c in cols_to_melt): return
    
    melted = df.melt(id_vars=['participant_id', 'condition'], value_vars=cols_to_melt, 
                     var_name='segment_col', value_name='value')
    # Clean segment name
    melted['segment'] = melted['segment_col'].str.replace(f"{base_col}_", "")
    
    all_vals = melted['value'].dropna()
    if all_vals.empty: return

    q1, q3 = all_vals.quantile(0.25), all_vals.quantile(0.75)
    iqr = q3 - q1
    zmin, zmax = max(all_vals.min(), q1 - 1.5*iqr), min(all_vals.max(), q3 + 1.5*iqr)

    for version, (ymin, ymax) in enumerate([(all_vals.min(), all_vals.max()), (zmin, zmax)]):
        plt.figure(figsize=(16, 12)) 
        ax = sns.boxplot(
            data=melted, x='segment', y='value', hue='condition', 
            order=segment_order, hue_order=condition_order, palette=CONDITION_COLORS, width=0.7,
            showfliers=(version==0), showmeans=True, meanline=True,
            meanprops={'linestyle': '--', 'linewidth': 1.5, 'color': 'black', 'alpha': 0.6},
            medianprops={'linestyle': '-', 'linewidth': 1.5, 'color': 'black'}
        )
        
        # Calculate stats positions
        # There are 3 groups (segments), and within each group 3 boxes (conditions)
        n_segments = len(segment_order)
        n_conditions = len(condition_order)
        
        # Calculate x offsets for hue
        width = 0.7
        offsets = np.linspace(-width/2 + width/(2*n_conditions), width/2 - width/(2*n_conditions), n_conditions)

        for i, seg in enumerate(segment_order):
            for j, cond in enumerate(condition_order):
                vals = melted[(melted['segment'] == seg) & (melted['condition'] == cond)]['value'].dropna()
                pos = i + offsets[j]
                
                # Jittered points
                x = np.random.normal(pos, 0.02, size=len(vals))
                plt.scatter(x, vals, alpha=0.3, s=15, color='black', edgecolors='white', zorder=3)
                
                # Stats text
                n, mu, sd = len(vals), vals.mean(), vals.std()
                stats_label = f"N={n}\nM={mu:.2f}\nS={sd:.2f}"
                ax.text(pos, -0.04, stats_label, transform=ax.get_xaxis_transform(),
                        ha='center', va='top', fontsize=8, linespacing=1.2)

        ax.set_xticklabels(segment_labels, fontsize=14, fontweight='bold')
        yr = max(ymax - ymin, 0.01)
        ax.set_ylim(ymin - 0.1 * yr, ymax + 0.3 * yr)
        plt.title(f"{title} by Track Segment{' (Zoomed)' if version==1 else ''}", fontweight='bold', fontsize=20)
        plt.ylabel(f"[{unit}]", fontsize=12)
        plt.xlabel('')
        plt.legend(title='Condition', loc='upper right')
        plt.subplots_adjust(bottom=0.25)
        
        path = full_folder if version == 0 else zoomed_folder
        plt.savefig(os.path.join(path, f"segmented_{base_col}.png"), dpi=200, bbox_inches='tight')
        plt.close()

print("Generating individual global plots...")
for col, title, unit in global_metrics:
    generate_global_plot(col, title, unit)

print("Generating segmented 9-box plots...")
for base, title, unit in segmented_bases:
    generate_segmented_plot(base, title, unit)

print("Process complete. Check the 'boxplots' folder.")