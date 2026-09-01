/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/. */

using System.Text.Json;
using System.Text.Json.Nodes;

namespace OpenCdsi.VaxEngine.Api;

/// <summary>
/// Implements the q/fields query parameters from the reference-data API's own spec:
///   q      - "Return only the objects containing the given value." e.g. /antigens/HepA/series?q=risk
///   fields - "Return only the fields named in the parameter." e.g. /vaccines?fields=cvx,shortDescription
///
/// Applied as ONE IEndpointFilter registered on the whole /api/v3 route group (see Program.cs),
/// rather than added individually to each of the 18 reference-data endpoints - this is a
/// cross-cutting concern that should apply uniformly, and any future /api/v3 endpoint gets it
/// automatically without needing to remember to wire it in by hand. Deliberately NOT applied to
/// /api/v3/forecast or /health - q/fields are this reference-data API's own contract, not a
/// general-purpose feature of every endpoint in this project.
///
/// Works on the endpoint's already-produced IResult, not by touching the 18 handlers themselves:
/// unwraps via IValueHttpResult (the interface Results.Ok&lt;T&gt; implements specifically so
/// code like this doesn't need to know T at compile time - the proper mechanism for this, not
/// reflection), re-serializes to a JsonNode, filters/projects, and returns the result as raw JSON
/// text. A result that ISN'T a successful value result (e.g. NotFound) passes through unchanged -
/// there's nothing to filter in a 404.
///
/// q only applies when the result is a JSON array - matching the spec's own example, which shows
/// it filtering a list endpoint, not a single-object one; it's a silent no-op otherwise, not an
/// error, since "filter this one object down to itself or nothing" isn't a meaningful operation.
/// fields applies to both a single object and an array of objects, and only ever touches an
/// object's own top-level properties - nested objects/arrays are returned as-is, not recursively
/// projected, since the spec's own example (fields=key,shortDescription) only shows flat names.
///
/// q is applied BEFORE fields when both are present - filtering, then selecting columns, matches
/// the intuitive order and means q can still match against a field that fields would otherwise
/// have stripped from the final response.
/// </summary>
public sealed class QFieldsEndpointFilter : IEndpointFilter
{
    // Matches what ASP.NET Core's own minimal-API JSON serialization uses by default
    // (JsonSerializerDefaults.Web: camelCase property names, case-insensitive on read) - the
    // re-serialization here needs to produce the SAME shape the client would have gotten
    // without q/fields, just filtered/projected.
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var result = await next(context);

        var q = context.HttpContext.Request.Query["q"].FirstOrDefault();
        var fieldsRaw = context.HttpContext.Request.Query["fields"].FirstOrDefault();
        if (string.IsNullOrEmpty(q) && string.IsNullOrEmpty(fieldsRaw))
        {
            return result;
        }

        if (result is not IValueHttpResult { Value: { } value })
        {
            return result;
        }

        var node = JsonSerializer.SerializeToNode(value, SerializerOptions);
        if (node is null)
        {
            return result;
        }

        if (!string.IsNullOrEmpty(q) && node is JsonArray array)
        {
            node = FilterByQ(array, q);
        }

        if (!string.IsNullOrEmpty(fieldsRaw))
        {
            var fields = fieldsRaw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (fields.Length > 0)
            {
                node = ProjectFields(node, fields);
            }
        }

        return Results.Text(node.ToJsonString(SerializerOptions), "application/json");
    }

    private static JsonArray FilterByQ(JsonArray array, string q)
    {
        var filtered = new JsonArray();
        foreach (var element in array)
        {
            // Substring match against the element's own full serialized form - deliberately
            // simple (matches "an object containing the given value" literally, per the spec's
            // own wording) rather than restricted to specific fields, so a search term can match
            // anywhere in the object without the caller needing to know its shape in advance.
            if (element is not null && element.ToJsonString(SerializerOptions).Contains(q, StringComparison.OrdinalIgnoreCase))
            {
                filtered.Add(element.DeepClone());
            }
        }
        return filtered;
    }

    private static JsonNode ProjectFields(JsonNode node, string[] fields)
    {
        if (node is JsonArray array)
        {
            var projected = new JsonArray();
            foreach (var element in array)
            {
                projected.Add(element is JsonObject obj ? ProjectObject(obj, fields) : element?.DeepClone());
            }
            return projected;
        }
        return node is JsonObject singleObject ? ProjectObject(singleObject, fields) : node;
    }

    private static JsonObject ProjectObject(JsonObject obj, string[] fields)
    {
        var result = new JsonObject();
        foreach (var field in fields)
        {
            // Case-insensitive match against the real (camelCase) JSON property names, since a
            // caller typing "fields=ShortDescription" or "fields=shortdescription" almost
            // certainly means the same field as the API's own "shortDescription" - being strict
            // here would just be a surprising way to silently drop a field the caller clearly
            // asked for.
            var match = obj.FirstOrDefault(kv => string.Equals(kv.Key, field, StringComparison.OrdinalIgnoreCase));
            if (match.Key is not null)
            {
                result[match.Key] = match.Value?.DeepClone();
            }
        }
        return result;
    }
}
