/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/. */

namespace OpenCdsi.VaxEngine.Contracts;

/// <summary>Thrown for a well-formed-JSON-but-invalid-content request (e.g. an unrecognized Gender string) - caught by the endpoint handler and turned into a 400, distinct from an unexpected exception that should surface as a 500.</summary>
public sealed class InvalidRequestException(string message) : Exception(message);
