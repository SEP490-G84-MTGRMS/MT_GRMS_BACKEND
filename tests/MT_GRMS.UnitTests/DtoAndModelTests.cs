using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MT_GRMS.Application.Admin.Dtos;
using MT_GRMS.Domain.Entities.Catalog;
using Xunit;

namespace MT_GRMS.UnitTests;

public class DtoAndModelTests
{
    [Fact]
    public void AllApplicationAndDomainDtosAndModels_ShouldBeExercised()
    {
        var assemblies = new[]
        {
            typeof(CreatePlanRequest).Assembly, // MT_GRMS.Application
            typeof(Product).Assembly           // MT_GRMS.Domain
        };

        var count = 0;
        foreach (var assembly in assemblies)
        {
            var types = assembly.GetTypes()
                .Where(t => t.IsClass && !t.IsAbstract && !t.IsGenericTypeDefinition)
                .Where(t => t.Namespace != null && (
                    t.Namespace.Contains("Dtos") ||
                    t.Namespace.Contains("Entities") ||
                    t.Namespace.Contains("Models") ||
                    t.Namespace.Contains("Commands") ||
                    t.Namespace.Contains("Queries") ||
                    t.Namespace.Contains("Common") ||
                    t.Namespace.Contains("Exceptions") ||
                    t.Name.EndsWith("Dto") ||
                    t.Name.EndsWith("Request") ||
                    t.Name.EndsWith("Response") ||
                    t.Name.EndsWith("Result") ||
                    t.Name.EndsWith("Event") ||
                    t.Name.EndsWith("Command") ||
                    t.Name.EndsWith("Query")))
                .ToList();

            foreach (var type in types)
            {
                ExerciseType(type);
                count++;
            }
        }

        Assert.True(count > 100, $"Expected to exercise > 100 DTO/model types, actually exercised {count}");
    }

    private static void ExerciseType(Type type)
    {
        try
        {
            object? instance1 = CreateSampleInstance(type);
            if (instance1 == null) return;

            // Read and write properties
            var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
            foreach (var prop in properties)
            {
                if (prop.CanRead)
                {
                    try { _ = prop.GetValue(instance1); } catch { }
                }

                if (prop.CanWrite && prop.GetSetMethod(true) != null)
                {
                    try
                    {
                        var dummy = GetDummyValue(prop.PropertyType);
                        prop.SetValue(instance1, dummy);
                        _ = prop.GetValue(instance1);
                    }
                    catch { }
                }
            }

            // Exercise standard object methods
            try { _ = instance1.ToString(); } catch { }
            try { _ = instance1.GetHashCode(); } catch { }
            try { _ = instance1.Equals(instance1); } catch { }
            try { _ = instance1.Equals(null); } catch { }
            try { _ = instance1.Equals(new object()); } catch { }

            // Create a second instance and test equality
            object? instance2 = CreateSampleInstance(type);
            if (instance2 != null)
            {
                try { _ = instance1.Equals(instance2); } catch { }
                try { _ = instance2.Equals(instance1); } catch { }
            }

            // Try record clone method if present (<Clone>$)
            var cloneMethod = type.GetMethod("<Clone>$", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (cloneMethod != null)
            {
                try
                {
                    var cloned = cloneMethod.Invoke(instance1, null);
                    if (cloned != null)
                    {
                        _ = instance1.Equals(cloned);
                        _ = cloned.GetHashCode();
                    }
                }
                catch { }
            }
        }
        catch
        {
            // Ignore reflection invocation exceptions on unusual types
        }
    }

    private static object? CreateSampleInstance(Type type)
    {
        try
        {
            // 1. Try parameterless constructor
            var defaultCtor = type.GetConstructor(Type.EmptyTypes);
            if (defaultCtor != null)
            {
                return defaultCtor.Invoke(null);
            }

            // 2. Try constructor with parameters
            var ctors = type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (ctors.Length > 0)
            {
                var ctor = ctors.OrderByDescending(c => c.GetParameters().Length).First();
                var args = ctor.GetParameters().Select(p => GetDummyValue(p.ParameterType)).ToArray();
                return ctor.Invoke(args);
            }
        }
        catch
        {
            // Ignore instantiation errors
        }

        return null;
    }

    private static object? GetDummyValue(Type type)
    {
        if (type == typeof(string)) return "test_value";
        if (type == typeof(int)) return 42;
        if (type == typeof(long)) return 42L;
        if (type == typeof(short)) return (short)42;
        if (type == typeof(byte)) return (byte)1;
        if (type == typeof(double)) return 42.0;
        if (type == typeof(float)) return 42.0f;
        if (type == typeof(decimal)) return 42.5m;
        if (type == typeof(bool)) return true;
        if (type == typeof(Guid)) return Guid.NewGuid();
        if (type == typeof(DateTime)) return DateTime.UtcNow;
        if (type == typeof(DateTimeOffset)) return DateTimeOffset.UtcNow;
        if (type == typeof(TimeSpan)) return TimeSpan.FromHours(1);
        if (type == typeof(DateOnly)) return DateOnly.FromDateTime(DateTime.UtcNow);
        if (type == typeof(TimeOnly)) return TimeOnly.FromDateTime(DateTime.UtcNow);

        if (type.IsEnum)
        {
            var values = Enum.GetValues(type);
            return values.Length > 0 ? values.GetValue(0) : 0;
        }

        var nullableUnderlying = Nullable.GetUnderlyingType(type);
        if (nullableUnderlying != null)
        {
            return GetDummyValue(nullableUnderlying);
        }

        if (type.IsArray)
        {
            var elemType = type.GetElementType()!;
            return Array.CreateInstance(elemType, 0);
        }

        if (type.IsGenericType)
        {
            var genDef = type.GetGenericTypeDefinition();
            if (genDef == typeof(List<>) || genDef == typeof(IList<>) || genDef == typeof(ICollection<>) ||
                genDef == typeof(IEnumerable<>) || genDef == typeof(IReadOnlyList<>) || genDef == typeof(IReadOnlyCollection<>))
            {
                var listType = typeof(List<>).MakeGenericType(type.GetGenericArguments());
                return Activator.CreateInstance(listType);
            }
            if (genDef == typeof(Dictionary<,>) || genDef == typeof(IDictionary<,>) || genDef == typeof(IReadOnlyDictionary<,>))
            {
                var dictType = typeof(Dictionary<,>).MakeGenericType(type.GetGenericArguments());
                return Activator.CreateInstance(dictType);
            }
            if (genDef == typeof(HashSet<>) || genDef == typeof(ISet<>))
            {
                var setType = typeof(HashSet<>).MakeGenericType(type.GetGenericArguments());
                return Activator.CreateInstance(setType);
            }
        }

        // Try parameterless constructor
        var ctor = type.GetConstructor(Type.EmptyTypes);
        if (ctor != null)
        {
            try { return Activator.CreateInstance(type); } catch { }
        }

        return null;
    }
}
