using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using Azure.Data.Tables;

namespace _10533342_AgriStreamIOT_Akonaho_Ravhura.Data
{
    /// <summary>
    /// Copies an object's simple properties (text, numbers, dates, enums...) to and from a Table Storage row.
    /// Navigation properties and collections are skipped; <see cref="AgriDataStore"/> fills them in when needed.
    /// Table Storage has no decimal type, so decimals are stored as doubles; enums are stored by name.
    /// </summary>
    public static class TableMapper
    {
        private static readonly ConcurrentDictionary<Type, PropertyInfo[]> Properties = new();

        private static PropertyInfo[] PropertiesOf(Type type) => Properties.GetOrAdd(type, t => t
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.CanWrite && p.GetIndexParameters().Length == 0 && IsSimple(p.PropertyType))
            .ToArray());

        private static bool IsSimple(Type type)
        {
            type = Nullable.GetUnderlyingType(type) ?? type;
            return type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal)
                || type == typeof(DateTime) || type == typeof(DateTimeOffset) || type == typeof(Guid);
        }

        public static TableEntity ToEntity(object item, string partitionKey, string rowKey)
        {
            var entity = new TableEntity(partitionKey, rowKey);
            foreach (var property in PropertiesOf(item.GetType()))
            {
                var value = property.GetValue(item);
                if (value == null) continue;
                entity[property.Name] = value switch
                {
                    decimal d => (double)d,
                    Enum e => e.ToString(),
                    DateTime dt => dt.Kind switch
                    {
                        DateTimeKind.Unspecified => DateTime.SpecifyKind(dt, DateTimeKind.Utc),
                        DateTimeKind.Local => dt.ToUniversalTime(),
                        _ => dt
                    },
                    _ => value
                };
            }
            return entity;
        }

        public static T FromEntity<T>(TableEntity entity) where T : new()
        {
            var item = new T();
            foreach (var property in PropertiesOf(typeof(T)))
            {
                if (!entity.TryGetValue(property.Name, out var raw) || raw == null) continue;
                var target = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
                property.SetValue(item, Convert(raw, target));
            }
            return item;
        }

        private static object Convert(object raw, Type target)
        {
            if (target.IsEnum) return Enum.Parse(target, raw.ToString()!);
            if (target == typeof(decimal)) return (decimal)System.Convert.ToDouble(raw, CultureInfo.InvariantCulture);
            if (target == typeof(DateTime))
                return raw is DateTimeOffset dto ? dto.UtcDateTime : DateTime.SpecifyKind((DateTime)raw, DateTimeKind.Utc);
            if (target == typeof(DateTimeOffset))
                return raw is DateTime dt ? new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc)) : raw;
            if (target == typeof(Guid)) return raw is Guid g ? g : Guid.Parse(raw.ToString()!);
            return System.Convert.ChangeType(raw, target, CultureInfo.InvariantCulture);
        }
    }
}
