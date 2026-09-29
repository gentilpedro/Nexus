// Keyed by canvasId (not a single module-level instance) — Home.razor and
// WorkspaceDetail.razor import this same module and can both be alive at once under
// Blazor's enhanced navigation (no full page reload between them), so a shared
// singleton instance let one page's dispose destroy the other page's chart.
const chartInstances = new Map();

// Design tokens are declared with light-dark(), so reading a custom property returns the raw
// "light-dark(#a, #b)" text, not a color Chart.js can paint. Resolving through a probe element's
// computed `color` gives the value for the theme that is actually on screen.
function resolveColor(token, fallback) {
    const probe = document.createElement('span');
    probe.style.color = `var(${token})`;
    probe.style.display = 'none';
    document.body.appendChild(probe);
    const color = getComputedStyle(probe).color;
    probe.remove();
    return color || fallback;
}

function buildChart(canvas, labels, values) {
    const accent = resolveColor('--chart-1', '#5B5CEB');
    const muted = resolveColor('--color-text-muted', '#6B7280');
    const grid = resolveColor('--color-divider', '#EEF2F7');
    const today = labels.length - 1;

    return new Chart(canvas, {
        type: 'bar',
        data: {
            labels: labels,
            datasets: [{
                label: 'Concluídas',
                data: values,
                // Today's bar at full strength, the previous days a step back: the eye lands on
                // "how am I doing today" first, then reads the week as context.
                backgroundColor: values.map((_, i) => i === today ? accent : accent.replace('rgb(', 'rgba(').replace(')', ', 0.45)')),
                borderRadius: 4,
                borderSkipped: false,
                maxBarThickness: 28
            }]
        },
        options: {
            responsive: true,
            maintainAspectRatio: false,
            animation: window.matchMedia('(prefers-reduced-motion: reduce)').matches ? false : { duration: 300 },
            plugins: {
                legend: { display: false },
                tooltip: {
                    intersect: false,
                    mode: 'index',
                    displayColors: false,
                    callbacks: {
                        label: (ctx) => `${ctx.parsed.y} ${ctx.parsed.y === 1 ? 'tarefa concluída' : 'tarefas concluídas'}`
                    }
                }
            },
            scales: {
                x: {
                    ticks: { color: muted, font: { size: 11 } },
                    grid: { display: false },
                    border: { display: false }
                },
                y: {
                    beginAtZero: true,
                    ticks: { color: muted, stepSize: 1, precision: 0, font: { size: 11 } },
                    grid: { color: grid },
                    border: { display: false }
                }
            }
        }
    });
}

export function render(canvasId, labels, values) {
    const canvas = document.getElementById(canvasId);
    if (!canvas) {
        return;
    }

    destroy(canvasId);
    const entry = { labels, values, chart: buildChart(canvas, labels, values) };

    // Repaint with the other theme's colors when the person toggles light/dark.
    entry.observer = new MutationObserver(() => {
        const current = document.getElementById(canvasId);
        if (!current) {
            return;
        }
        entry.chart.destroy();
        entry.chart = buildChart(current, entry.labels, entry.values);
    });
    entry.observer.observe(document.documentElement, { attributes: true, attributeFilter: ['data-theme'] });

    chartInstances.set(canvasId, entry);
}

export function destroy(canvasId) {
    const entry = chartInstances.get(canvasId);
    if (entry) {
        entry.observer?.disconnect();
        entry.chart.destroy();
        chartInstances.delete(canvasId);
    }
}
