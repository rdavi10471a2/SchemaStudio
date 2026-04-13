namespace SchemaStudioWebViewer.Utils
{
    using SchemaStudioWebViewer.Models;
    using System;
    using System.Collections.Concurrent;
    using System.Linq;
    using System.Reflection;

    public class AttributeService
    {
        private readonly ConcurrentDictionary<string, string> _cache = new();

        // Keep the generic versions for code-behind usage
        public string Name<T>(string prop) => Get(typeof(T).Name, prop, "Name", () => ReflectionUtils.GetDisplayName<T>(prop));
        public string Description<T>(string prop) => Get(typeof(T).Name, prop, "Desc", () => ReflectionUtils.GetDescription<T>(prop));

        // String-based "sort it out" methods
        public string GetName(string qualifiedName) => ResolveFromPath(qualifiedName, "Name");
        public string GetDesc(string qualifiedName) => ResolveFromPath(qualifiedName, "Desc");

        private string ResolveFromPath(string path, string suffix)
        {
            if (string.IsNullOrWhiteSpace(path) || !path.Contains('.'))
                return path;

            var key = $"{path}.{suffix}";

            return _cache.GetOrAdd(key, _ =>
            {
                var parts = path.Split('.');
                var typeName = parts[0];
                var propName = parts[1];

                // Find type in the assembly
                var type = typeof(SchemaObjectModel).Assembly.GetTypes()
                    .FirstOrDefault(t => t.Name == typeName || t.FullName == $"SchemaStudioWebViewer.Models.{typeName}");

                if (type != null)
                {
                    // Map "Name" -> GetDisplayName and "Desc" -> GetDescription
                    string methodName = suffix == "Name" ? "GetDisplayName" : "GetDescription";

                    return InvokeGenericReflectionUtil(type, methodName, propName) ?? path;
                }

                return path;
            });
        }

        private string? InvokeGenericReflectionUtil(Type modelType, string methodName, string propName)
        {
            try
            {
                // Get the generic method from ReflectionUtils
                var method = typeof(ReflectionUtils)
                    .GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .FirstOrDefault(m => m.Name == methodName && m.IsGenericMethod);

                if (method != null)
                {
                    // Supply the generic type (T) and invoke
                    var genericMethod = method.MakeGenericMethod(modelType);
                    return genericMethod.Invoke(null, new object[] { propName })?.ToString();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Metadata Error: {ex.Message}");
            }
            return null;
        }

        private string Get(string type, string prop, string suffix, Func<string> fetcher)
        {
            var key = $"{type}.{prop}.{suffix}";
            return _cache.GetOrAdd(key, _ => fetcher() ?? "");
        }
    }
}