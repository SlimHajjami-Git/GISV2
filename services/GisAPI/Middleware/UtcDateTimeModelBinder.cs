using System.Globalization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace GisAPI.Middleware;

/// <summary>
/// Custom model binder that ensures all DateTime/DateTime? query parameters
/// are properly converted to UTC. Fixes the issue where ASP.NET Core's default
/// model binding converts ISO 8601 UTC strings (with Z suffix) to local time,
/// and then SpecifyKind re-labels without converting — causing a timezone offset.
/// </summary>
public class UtcDateTimeModelBinder : IModelBinder
{
    public Task BindModelAsync(ModelBindingContext bindingContext)
    {
        var valueProviderResult = bindingContext.ValueProvider.GetValue(bindingContext.ModelName);
        if (valueProviderResult == ValueProviderResult.None)
            return Task.CompletedTask;

        var value = valueProviderResult.FirstValue;
        if (string.IsNullOrWhiteSpace(value))
        {
            bindingContext.Result = ModelBindingResult.Success(null);
            return Task.CompletedTask;
        }

        if (DateTime.TryParse(value, CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var dt))
        {
            bindingContext.Result = ModelBindingResult.Success(dt);
        }
        else
        {
            bindingContext.ModelState.TryAddModelError(bindingContext.ModelName, $"Invalid date: {value}");
        }

        return Task.CompletedTask;
    }
}

public class UtcDateTimeModelBinderProvider : IModelBinderProvider
{
    public IModelBinder? GetBinder(ModelBinderProviderContext context)
    {
        if (context.Metadata.ModelType == typeof(DateTime) ||
            context.Metadata.ModelType == typeof(DateTime?))
        {
            return new UtcDateTimeModelBinder();
        }
        return null;
    }
}
