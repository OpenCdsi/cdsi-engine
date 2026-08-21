using System.Runtime.CompilerServices;

// Allows the test project to exercise internal helpers (e.g. EvaluatePreferableInterval's
// GroupByReferencePoint) directly, without making them part of Cdsi.Core's public API surface.
[assembly: InternalsVisibleTo("Cdsi.Core.Tests")]
