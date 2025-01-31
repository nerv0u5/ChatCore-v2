using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using ChatCore.Utilities;

namespace ChatCore.Config
{
	public class ObjectSerializer
	{
		private static readonly Regex ConfigRegex = new Regex(
				@"(?<Section>\[[a-zA-Z0-9\s]+\])|(?<Name>[^=\/\/#\s]+)\s*=[\t\p{Zs}]*(?<Value>"".+""|({(?:[^{}]|(?<Array>{)|(?<-Array>}))+(?(Array)(?!))})|\S+)?[\t\p{Zs}]*((\/{2,2}|[#])(?<Comment>.+)?)?",
				RegexOptions.Compiled | RegexOptions.Multiline);

		private static readonly ConcurrentDictionary<Type, Func<FieldInfo, string, object>> ConvertFromString = new ConcurrentDictionary<Type, Func<FieldInfo, string, object>>();
		private static readonly ConcurrentDictionary<Type, Func<FieldInfo, object, string>> ConvertToString = new ConcurrentDictionary<Type, Func<FieldInfo, object, string>>();
		private readonly ConcurrentDictionary<string, string> _comments = new ConcurrentDictionary<string, string>();

		private static void InitTypeHandlers()
		{
			// String handlers
			ConvertFromString.TryAdd(typeof(string), (_, value) => (value.StartsWith("\"") && value.EndsWith("\"") ? value.Substring(1, value.Length - 2) : value));
			ConvertToString.TryAdd(typeof(string), (fieldInfo, obj) =>
			{
				var value = (string)obj.GetFieldValue(fieldInfo.Name);
				// If the value is an array, we don't need quotes
				if (value.StartsWith("{") && value.EndsWith("}"))
				{
					return value;
				}

				return $"\"{value}\"";
			});

			// Bool handlers
			ConvertFromString.TryAdd(typeof(bool),
				(_, value) => (value.Equals("true", StringComparison.CurrentCultureIgnoreCase) || value.Equals("on", StringComparison.CurrentCultureIgnoreCase) || value.Equals("1")));
			ConvertToString.TryAdd(typeof(bool), (fieldInfo, obj) => ((bool)obj.GetFieldValue(fieldInfo.Name)).ToString());

			// Enum handlers
			ConvertFromString.TryAdd(typeof(Enum), (fieldInfo, value) => Enum.Parse(fieldInfo.FieldType, value));
			ConvertToString.TryAdd(typeof(Enum), (fieldInfo, obj) => obj.GetFieldValue(fieldInfo.Name).ToString());

			// DateTime handler
			ConvertFromString.TryAdd(typeof(DateTime), (_, value) => DateTime.FromFileTimeUtc(long.Parse(value)));
			ConvertToString.TryAdd(typeof(DateTime), (fieldInfo, obj) => ((DateTime)obj.GetFieldValue(fieldInfo.Name)).ToFileTimeUtc().ToString());


			// List<string> handlers
			ConvertFromString.TryAdd(typeof(List<string>), (_, value) =>
			{
				if (value.StartsWith("\"") && value.EndsWith("\""))
				{
					value = value.Substring(1, value.Length - 2);
				}

				return string.IsNullOrEmpty(value) ? new List<string>() : new List<string>(value.Replace(" ", "").ToLower().TrimEnd(',').Split(','));
			});

			ConvertToString.TryAdd(typeof(List<string>), (fieldInfo, obj) => "\"" + string.Join(",", (List<string>)obj.GetFieldValue(fieldInfo.Name)).Replace(" ", "").ToLower().TrimEnd(',') + "\"");
		}

		private static bool CreateDynamicFieldConverter(FieldInfo fieldInfo)
		{
			var fieldType = fieldInfo.FieldType;
			if (TryCreateFieldConverterFromParseFunction(fieldInfo))
			{
				return true;
			}

			if (fieldType.IsArray)
			{
				return false;
			}

			// If we got here, there's no nice convenient Parse function for this type... so try to convert each field individually.
			var fields = fieldInfo.FieldType.GetRuntimeFields();
			foreach (var field in fields)
			{
				if (!field.IsPrivate && !field.IsStatic && !TryCreateFieldConverterFromParseFunction(field))
				{
					throw new Exception($"Unsupported type {fieldInfo.FieldType.Name} or one of the types it implements cannot be automatically converted by the ObjectSerializer!");
				}
			}

			ConvertFromString.TryAdd(fieldType, (fi, v) =>
			{
				var json = JSON.Parse(v);
				var obj = Activator.CreateInstance(fi.FieldType);
				foreach (var subFieldInfo in fi.FieldType.GetRuntimeFields())
				{
					if (!subFieldInfo.IsPrivate && !subFieldInfo.IsStatic)
					{
						subFieldInfo.SetValue(obj, ConvertFromString[subFieldInfo.FieldType].Invoke(subFieldInfo, json[subFieldInfo.Name].Value));
					}
				}

				return obj;
			});

			ConvertToString.TryAdd(fieldType, (fi, v) =>
			{
				var json = new JSONObject();
				// Grab the current field we're trying to convert off the parent object
				var currentField = v.GetFieldValue(fi.Name);
				foreach (var subFieldInfo in fi.FieldType.GetRuntimeFields())
				{
					if (!subFieldInfo.IsPrivate && !subFieldInfo.IsStatic)
					{
						var value = ConvertToString[subFieldInfo.FieldType].Invoke(subFieldInfo, currentField);
						json.Add(subFieldInfo.Name, new JSONString(value));
					}
				}

				return json.ToString();
			});

			return true;
		}

		private static bool TryCreateFieldConverterFromParseFunction(FieldInfo fieldInfo)
		{
			var fieldType = fieldInfo.FieldType;
			if (ConvertFromString.ContainsKey(fieldType) && ConvertToString.ContainsKey(fieldType))
			{
				// Converters already exist for these types
				return true;
			}

			var functions = fieldType.GetRuntimeMethods();
			foreach (var func in functions)
			{
				switch (func.Name)
				{
					case "Parse":
						var parameters = func.GetParameters();
						if (parameters.Count() != 1 || parameters[0].ParameterType != typeof(string))
						{
							// If the function doesn't have only a single parameter of type string, don't use this function as a field converter.
							continue;
						}

						if (func.ReturnType != fieldType)
						{
							// If the function doesn't return the type of the field we're creating the converter for, don't use this function as a field converter.
							continue;
						}

						ConvertFromString.TryAdd(fieldType, (_, v) => func.Invoke(null, new object[]
						{
							v
						}));
						ConvertToString.TryAdd(fieldType, (fi, v) => v.GetFieldValue(fi.Name).ToString());

						return true;
				}
			}

			return false;
		}

		public void Load(object obj, string path)
		{
			if (ConvertFromString.Count == 0)
			{
				InitTypeHandlers();
			}

			var backupPath = path + ".bak";
			if (File.Exists(backupPath) && !File.Exists(path))
			{
				File.Move(backupPath, path);
			}

			if (File.Exists(path))
			{
				var matches = ConfigRegex.Matches(File.ReadAllText(path));
				foreach (Match match in matches)
				{
					if (match.Groups["Section"].Success)
					{
						continue;
					}

					// Grab the name, which has to exist or the regex wouldn't have matched
					var name = match.Groups["Name"].Value;

					// Check if any comments existed
					if (match.Groups["Comment"].Success)
					{
						// Then store them in memory so we can write them back later on
						_comments[name] = match.Groups["Comment"].Value.TrimEnd('\n', '\r');
					}

					// If there's no value, continue on at this point
					if (!match.Groups["Value"].Success)
					{
						continue;
					}

					var value = match.Groups["Value"].Value;

					// Otherwise, read the value in with the appropriate handler
					var fieldInfo = obj.GetType().GetField(name.Replace(".", "_"));

					if (fieldInfo == null)
					{
						// Skip missing fields, in case one was changed or removed.
						continue;
					}

					// If the fieldType is an enum, replace it with the generic Enum type
					var fieldType = fieldInfo.FieldType.IsEnum ? typeof(Enum) : fieldInfo.FieldType;

					// Invoke our ConvertFromString method if it exists
					if (!ConvertFromString.TryGetValue(fieldType, out var convertFromString))
					{
						if (CreateDynamicFieldConverter(fieldInfo))
						{
							convertFromString = ConvertFromString[fieldType];
						}
					}

					try
					{
						var converted = convertFromString.Invoke(fieldInfo, value);
						fieldInfo.SetValue(obj, converted);
					}
					catch (Exception)
					{
						//Plugin.Log($"Failed to parse field {name} with value {value} as type {fieldInfo.FieldType.Name}. {ex.ToString()}");
					}
				}
			}
		}

		public void Save(object obj, string path)
		{
			if (ConvertToString.Count == 0)
			{
				InitTypeHandlers();
			}

			var backupPath = path + ".bak";
			if (File.Exists(path))
			{
				if (File.Exists(backupPath))
				{
					File.Delete(backupPath);
				}

				File.Move(path, backupPath);
			}

			string? lastConfigSection = null;
			var serializedClass = new List<string>();

			if (obj.GetType().GetCustomAttribute(typeof(ConfigHeader)) is ConfigHeader configHeader)
			{
				foreach (var comment in configHeader.Comment)
				{
					serializedClass.Add(string.IsNullOrWhiteSpace(comment) ? comment : $"// {comment}");
				}
			}

			foreach (var fieldInfo in obj.GetType().GetFields())
			{
				// If the fieldType is an enum, replace it with the generic Enum type
				var fieldType = fieldInfo.FieldType.IsEnum ? typeof(Enum) : fieldInfo.FieldType;

				// Invoke our convertFromString method if it exists
				if (!ConvertToString.TryGetValue(fieldType, out var convertToString))
				{
					if (CreateDynamicFieldConverter(fieldInfo))
					{
						convertToString = ConvertToString[fieldType];
					}
				}

				if (fieldInfo.GetCustomAttribute(typeof(ConfigSection)) is ConfigSection configSection && !string.IsNullOrEmpty(configSection.Name))
				{
					if (lastConfigSection != null && configSection.Name != lastConfigSection)
					{
						serializedClass.Add("");
					}

					serializedClass.Add($"[{configSection.Name}]");
					lastConfigSection = configSection.Name;
				}

				var configMeta = (ConfigMeta?)fieldInfo.GetCustomAttribute(typeof(ConfigMeta));
				var valueStr = "";
				try
				{
					if (!_comments.TryGetValue(fieldInfo.Name, out var comment))
					{
						// If the user hasn't entered any of their own comments, use the default one of it exists
						if (configMeta != null && !string.IsNullOrEmpty(configMeta.Comment))
						{
							comment = configMeta.Comment;
						}
					}

					valueStr = $"{convertToString.Invoke(fieldInfo, obj)}{(comment != null ? " //" + comment : "")}";
				}
				catch (Exception)
				{
					//throw;
					//Plugin.Log($"Failed to convert field {fieldInfo.Name} to string! Value type is {fieldInfo.FieldType.Name}. {ex.ToString()}");
				}

				serializedClass.Add($"{fieldInfo.Name.Replace("_", ".")}={valueStr}");
			}

			if (!string.IsNullOrEmpty(path) && serializedClass.Count > 0)
			{
				var dirName = Path.GetDirectoryName(path);
				if (dirName != null && !Directory.Exists(dirName))
				{
					Directory.CreateDirectory(dirName);
				}

				var tmpPath = $"{path}.tmp";
				File.WriteAllLines(tmpPath, serializedClass.ToArray());
				if (File.Exists(path))
				{
					File.Delete(path);
				}

				File.Move(tmpPath, path);
			}
		}

		/// <summary>
		/// Returns a JSONObject containing each configurable value.
		/// </summary>
		/// <param name="obj">The object to serialize into json</param>
		/// <returns></returns>
		public JSONObject GetSettingsAsJson(object obj)
		{
			var jsonObject = new JSONObject();
			foreach (var fieldInfo in obj.GetType().GetFields())
			{
				if (fieldInfo.GetCustomAttribute(typeof(HtmlIgnore)) != null)
				{
					// Skip any fields with the HTMLIgnore attribute
					continue;
				}

				// If the fieldType is an enum, replace it with the generic Enum type

				var value = fieldInfo.GetValue(obj);
				if (value == null)
				{
					continue;
				}

				if (fieldInfo.FieldType.IsEnum)
				{
					jsonObject.Add(fieldInfo.Name, fieldInfo.GetValue(obj).ToString());
				}
				else
				{
					JSONNode? jsonValue = value switch
					{
						bool b => new JSONBool(b),
						int i => new JSONNumber(i),
						string s => new JSONString(s),
						List<string> ls => new JSONArray(ls),
						_ => null
					};

					if (jsonValue != null)
					{
						jsonObject.Add(fieldInfo.Name, jsonValue);
					}
				}
			}

			return jsonObject;
		}

		/// <summary>
		/// Sets class values from a web post request
		/// </summary>
		/// <param name="obj"></param>
		/// <param name="postData"></param>
		public void SetFromDictionary(object obj, JSONObject postData)
		{
			foreach (var kvp in postData)
			{
				// Otherwise, read the value in with the appropriate handler
				var fieldInfo = obj.GetType().GetField(kvp.Key.Replace(".", "_"));

				if (fieldInfo == null)
				{
					// Skip missing fields, in case one was changed or removed.
					continue;
				}

				// If the fieldType is an enum, replace it with the generic Enum type
				var fieldType = fieldInfo.FieldType.IsEnum ? typeof(Enum) : fieldInfo.FieldType;

				if (fieldType == typeof(bool))
				{
					fieldInfo.SetValue(obj, kvp.Value.AsBool);
				}
				else if (fieldType == typeof(int))
				{
					fieldInfo.SetValue(obj, kvp.Value.AsInt);
				}
				else if (fieldType == typeof(string))
				{
					fieldInfo.SetValue(obj, kvp.Value.Value);
				}
				else if (fieldType == typeof(Enum))
				{
					try
					{
						fieldInfo.SetValue(obj, Enum.Parse(fieldType, kvp.Value.ToString()));
					}
					catch (Exception)
					{
						// NOP
					}
				}
				else if (fieldInfo == typeof(List<string>))
				{
					var result = kvp.Value.AsArray?.Children.Select(x => x.ToString()).ToList();
					if (result != null)
					{
						fieldInfo.SetValue(obj, result);
					}
				}
			}
		}
	}
}