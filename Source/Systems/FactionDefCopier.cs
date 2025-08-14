using RimWorld;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Verse;

namespace LemProgress.Systems
{
    public class FactionDefCopier
    {
        private readonly Faction targetFaction;
        private readonly FactionDef originalDef;
        private readonly FactionDef sourceDef;
        private readonly HashSet<string> preservedFields;

        public FactionDefCopier(Faction targetFaction, FactionDef originalDef, FactionDef sourceDef)
        {
            this.targetFaction = targetFaction;
            this.originalDef = originalDef;
            this.sourceDef = sourceDef;
            this.preservedFields = GetPreservedFields();
        }

        public void Execute()
        {
            var preservedValues = StorePreservedValues();
            CopyFields();
            RestorePreservedValues(preservedValues);
            RebuildPawnGroupMakers();

            ModCore.LogDebug("Copied def from " + sourceDef.defName + " to " + targetFaction.Name);
        }

        private HashSet<string> GetPreservedFields()
        {
            var settings = ModCore.Settings;
            var fields = new HashSet<string>();

            // Always preserve core identity
            fields.Add("defName");
            fields.Add("shortHash");
            fields.Add("index");

            if (settings.preserveFactionNames)
            {
                fields.Add("label");
                fields.Add("fixedName");
            }

            if (settings.preserveFactionDescriptions)
            {
                fields.Add("description");
            }

            if (settings.preserveFactionRelations)
            {
                fields.Add("permanentEnemy");
                fields.Add("naturalEnemy");
                fields.Add("permanentEnemyStartingGoodwill");
                fields.Add("naturalEnemyGoodwillThreshold");
                fields.Add("goodwillDailyGain");
                fields.Add("goodwillDailyFall");
            }

            if (settings.preserveFactionColors)
            {
                fields.Add("colorSpectrum");
                fields.Add("defaultPawnGroupMakerColorSpectrum");
            }

            if (settings.preserveFactionIcons)
            {
                fields.Add("factionIcon");
                fields.Add("factionIconPath");
                fields.Add("settlementTexture");
                fields.Add("settlementTexturePath");
            }

            return fields;
        }

        private Dictionary<string, object> StorePreservedValues()
        {
            var values = new Dictionary<string, object>();

            foreach (var fieldName in preservedFields)
            {
                var field = GetField(fieldName);
                if (field != null)
                {
                    try
                    {
                        var value = field.GetValue(originalDef);
                        if (value != null)
                        {
                            values[fieldName] = value;
                        }
                    }
                    catch (Exception e)
                    {
                        ModCore.LogDebug("Failed to store field " + fieldName + ": " + e.Message);
                    }
                }

                // Also check properties
                var prop = GetProperty(fieldName);
                if (prop != null && prop.CanRead)
                {
                    try
                    {
                        var value = prop.GetValue(originalDef, null);
                        if (value != null && !values.ContainsKey(fieldName))
                        {
                            values[fieldName] = value;
                        }
                    }
                    catch (Exception e)
                    {
                        ModCore.LogDebug("Failed to store property " + fieldName + ": " + e.Message);
                    }
                }
            }

            return values;
        }

        private void CopyFields()
        {
            var fields = typeof(FactionDef).GetFields(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            foreach (var field in fields)
            {
                if (preservedFields.Contains(field.Name)) continue;
                if (field.Name == "pawnGroupMakers") continue; // Handle separately

                try
                {
                    var value = field.GetValue(sourceDef);

                    if (value is IList && !(value is string))
                    {
                        value = DeepCopyList((IList)value, field.FieldType);
                    }

                    field.SetValue(originalDef, value);
                }
                catch (Exception e)
                {
                    Log.Warning("[" + ModCore.ModId + "] Failed to copy field " + field.Name + ": " + e.Message);
                }
            }

            // Also copy properties
            var properties = typeof(FactionDef).GetProperties(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            foreach (var prop in properties)
            {
                if (!prop.CanRead || !prop.CanWrite) continue;
                if (preservedFields.Contains(prop.Name)) continue;

                try
                {
                    var value = prop.GetValue(sourceDef, null);
                    prop.SetValue(originalDef, value, null);
                }
                catch (Exception e)
                {
                    ModCore.LogDebug("Failed to copy property " + prop.Name + ": " + e.Message);
                }
            }
        }

        private void RestorePreservedValues(Dictionary<string, object> values)
        {
            foreach (var kvp in values)
            {
                var field = GetField(kvp.Key);
                if (field != null)
                {
                    try
                    {
                        field.SetValue(originalDef, kvp.Value);
                    }
                    catch (Exception e)
                    {
                        ModCore.LogDebug("Failed to restore field " + kvp.Key + ": " + e.Message);
                    }
                    continue;
                }

                var prop = GetProperty(kvp.Key);
                if (prop != null && prop.CanWrite)
                {
                    try
                    {
                        prop.SetValue(originalDef, kvp.Value, null);
                    }
                    catch (Exception e)
                    {
                        ModCore.LogDebug("Failed to restore property " + kvp.Key + ": " + e.Message);
                    }
                }
            }
        }

        private void RebuildPawnGroupMakers()
        {
            if (sourceDef.pawnGroupMakers == null) return;

            originalDef.pawnGroupMakers = new List<PawnGroupMaker>();

            foreach (var sourcePGM in sourceDef.pawnGroupMakers)
            {
                var newPGM = DeepCopyPawnGroupMaker(sourcePGM);
                originalDef.pawnGroupMakers.Add(newPGM);
            }

            ModCore.LogDebug("Rebuilt " + originalDef.pawnGroupMakers.Count + " pawn group makers");
        }

        private PawnGroupMaker DeepCopyPawnGroupMaker(PawnGroupMaker source)
        {
            var copy = new PawnGroupMaker();

            var fields = typeof(PawnGroupMaker).GetFields(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            foreach (var field in fields)
            {
                try
                {
                    var value = field.GetValue(source);

                    if (value is IList && !(value is string))
                    {
                        value = DeepCopyList((IList)value, field.FieldType);
                    }

                    field.SetValue(copy, value);
                }
                catch (Exception e)
                {
                    Log.Warning("[" + ModCore.ModId + "] Failed to copy PawnGroupMaker field " +
                        field.Name + ": " + e.Message);
                }
            }

            return copy;
        }

        private object DeepCopyList(IList source, Type fieldType)
        {
            if (source == null) return null;

            Type elementType;
            if (fieldType.IsArray)
            {
                elementType = fieldType.GetElementType();
            }
            else
            {
                var genericArgs = fieldType.GetGenericArguments();
                elementType = genericArgs.Length > 0 ? genericArgs[0] : typeof(object);
            }

            var listType = typeof(List<>).MakeGenericType(elementType);
            var newList = (IList)Activator.CreateInstance(listType);

            foreach (var item in source)
            {
                newList.Add(item);
            }

            if (fieldType.IsArray)
            {
                var array = Array.CreateInstance(elementType, newList.Count);
                newList.CopyTo(array, 0);
                return array;
            }

            return newList;
        }

        private FieldInfo GetField(string name)
        {
            return typeof(FactionDef).GetField(name,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        }

        private PropertyInfo GetProperty(string name)
        {
            return typeof(FactionDef).GetProperty(name,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        }
    }
}
