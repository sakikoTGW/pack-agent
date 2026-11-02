using System.Runtime.CompilerServices;

// Unit tests live beside the app so they can exercise planner internals (cookie minting,
// registry merge helpers) without shipping a testable public surface.
[assembly: InternalsVisibleTo("Pad.Tests")]
