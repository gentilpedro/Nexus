// JS initializer: Blazor finds and loads this module by its name ({assembly}.lib.module.js), so
// App.razor has no <script> tag for it. This is the pattern Microsoft Learn documents for running
// JS after enhanced navigation ("ASP.NET Core Blazor JavaScript with static server-side
// rendering"):
// https://learn.microsoft.com/aspnet/core/blazor/javascript-interoperability/static-server-rendering
//
// enhancedload exists only in Blazor's own event registry. document.addEventListener('enhancedload')
// never fires, which is why the settings tab used to stay off screen on phones (#130).
//
// Blazor raises it synchronously right after it patches the DOM, before the browser paints, so
// reapplying the theme here never shows a frame without it.
export function afterWebStarted(blazor) {
    blazor.addEventListener('enhancedload', () => {
        window.nexusApplyTheme();
        window.nexusRevealActiveSettingsTab();
    });
}
