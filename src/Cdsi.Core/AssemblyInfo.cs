/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/. */

using System.Runtime.CompilerServices;

// Allows the test project to exercise internal helpers (e.g. EvaluatePreferableInterval's
// GroupByReferencePoint) directly, without making them part of Cdsi.Core's public API surface.
[assembly: InternalsVisibleTo("Cdsi.Core.Tests")]
