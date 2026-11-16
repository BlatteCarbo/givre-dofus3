using System.Runtime.CompilerServices;

// Only the named offline harness may exercise the internal verified state
// transition. Never exposes it through the application/UI public API.
[assembly: InternalsVisibleTo("MZP13StateHarness")]
