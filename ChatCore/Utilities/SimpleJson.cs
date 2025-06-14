/* * * * *
 * A simple JSON Parser / builder
 * ------------------------------
 *
 * It mainly has been written as a simple JSON parser. It can build a JSON string
 * from the node-tree, or generate a node tree from any valid JSON string.
 *
 * Written by Bunny83
 * 2012-06-09
 *
 * Changelog now external. See Changelog.txt
 *
 * The MIT License (MIT)
 *
 * Copyright (c) 2012-2019 Markus Göbel (Bunny83)
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in all
 * copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
 * SOFTWARE.
 *
 * * * * */

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace ChatCore.Utilities
{
	// ReSharper disable once InconsistentNaming
	public enum JSONNodeType
	{
		Array = 1,
		Object = 2,
		String = 3,
		Number = 4,
		NullValue = 5,
		Boolean = 6,
		None = 7,
		Custom = 0xFF,
	}

	// ReSharper disable once InconsistentNaming
	public enum JSONTextMode
	{
		Compact,
		Indent
	}

	// ReSharper disable once InconsistentNaming
	public abstract partial class JSONNode
	{
		#region Enumerators

		public struct Enumerator
		{
			private readonly Type _type;

			private enum Type { None, Array, Object }

			private Dictionary<string, JSONNode>.Enumerator _mObject;
			private List<JSONNode>.Enumerator _mArray;

			public bool IsValid => _type != Type.None;

			public Enumerator(List<JSONNode>.Enumerator aArrayEnum)
			{
				_type = Type.Array;
				_mObject = default;
				_mArray = aArrayEnum;
			}

			public Enumerator(Dictionary<string, JSONNode>.Enumerator aDictEnum)
			{
				_type = Type.Object;
				_mObject = aDictEnum;
				_mArray = default;
			}

			public KeyValuePair<string, JSONNode> Current
			{
				get
				{
					if (_type == Type.Array)
					{
						return new KeyValuePair<string, JSONNode>(string.Empty, _mArray.Current);
					}

					if (_type == Type.Object)
					{
						return _mObject.Current;
					}

					return new KeyValuePair<string, JSONNode>(string.Empty, null!);
				}
			}

			public bool MoveNext()
			{
				if (_type == Type.Array)
				{
					return _mArray.MoveNext();
				}

				if (_type == Type.Object)
				{
					return _mObject.MoveNext();
				}

				return false;
			}
		}

		public struct ValueEnumerator
		{
			private Enumerator _mEnumerator;
			public ValueEnumerator(List<JSONNode>.Enumerator aArrayEnum) : this(new Enumerator(aArrayEnum)) { }
			public ValueEnumerator(Dictionary<string, JSONNode>.Enumerator aDictEnum) : this(new Enumerator(aDictEnum)) { }
			public ValueEnumerator(Enumerator aEnumerator) { _mEnumerator = aEnumerator; }
			public JSONNode Current => _mEnumerator.Current.Value;
			public bool MoveNext() { return _mEnumerator.MoveNext(); }
			public ValueEnumerator GetEnumerator() { return this; }
		}

		public struct KeyEnumerator
		{
			private Enumerator _mEnumerator;
			public KeyEnumerator(List<JSONNode>.Enumerator aArrayEnum) : this(new Enumerator(aArrayEnum)) { }
			public KeyEnumerator(Dictionary<string, JSONNode>.Enumerator aDictEnum) : this(new Enumerator(aDictEnum)) { }
			public KeyEnumerator(Enumerator aEnumerator) { _mEnumerator = aEnumerator; }
			public string Current => _mEnumerator.Current.Key;
			public bool MoveNext() { return _mEnumerator.MoveNext(); }
			public KeyEnumerator GetEnumerator() { return this; }
		}

		public class LinqEnumerator : IEnumerator<KeyValuePair<string, JSONNode>>, IEnumerable<KeyValuePair<string, JSONNode>>
		{
			private JSONNode _mNode;
			private Enumerator _mEnumerator;

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
			internal LinqEnumerator(JSONNode aNode)
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
			{
				_mNode = aNode;
				if (_mNode != null)
				{
					_mEnumerator = _mNode.GetEnumerator();
				}
			}

			public KeyValuePair<string, JSONNode> Current => _mEnumerator.Current;
			object IEnumerator.Current => _mEnumerator.Current;
			public bool MoveNext() { return _mEnumerator.MoveNext(); }

			public void Dispose()
			{
				_mNode = null!;
				_mEnumerator = new Enumerator();
			}

			public IEnumerator<KeyValuePair<string, JSONNode>> GetEnumerator()
			{
				return new LinqEnumerator(_mNode!);
			}

			public void Reset()
			{
				if (_mNode != null)
				{
					_mEnumerator = _mNode.GetEnumerator();
				}
			}

			IEnumerator IEnumerable.GetEnumerator()
			{
				return new LinqEnumerator(_mNode!);
			}
		}

		#endregion Enumerators

		#region common interface

		public static bool ForceAscii = false; // Use Unicode by default
		public static bool LongAsString = false; // lazy creator creates a JSONString instead of JSONNumber
		public static bool AllowLineComments = true; // allow "//"-style comments at the end of a line

		public abstract JSONNodeType Tag { get; }

		// ReSharper disable once ValueParameterNotUsed
		public virtual JSONNode this[int aIndex] { get => null!; set { } }

		// ReSharper disable once ValueParameterNotUsed
		public virtual JSONNode this[string aKey] { get => null!; set { } }

		// ReSharper disable once ValueParameterNotUsed
		public virtual string Value { get => ""; set { } }

		public virtual int Count => 0;

		public virtual bool IsNumber => false;
		public virtual bool IsString => false;
		public virtual bool IsBoolean => false;
		public virtual bool IsNull => false;
		public virtual bool IsArray => false;
		public virtual bool IsObject => false;

		// ReSharper disable once ValueParameterNotUsed
		public virtual bool Inline { get => false; set { } }

		public virtual void Add(string? aKey, JSONNode? aItem)
		{
		}

		public virtual void Add(JSONNode? anItem)
		{
			Add(string.Empty, anItem);
		}

		public virtual JSONNode? Remove(string aKey)
		{
			return null!;
		}

		public virtual JSONNode? Remove(int aIndex)
		{
			return null!;
		}

		public virtual JSONNode? Remove(JSONNode aNode)
		{
			return aNode;
		}

		public virtual JSONNode Clone()
		{
			return null!;
		}

		public virtual IEnumerable<JSONNode> Children
		{
			get
			{
				yield break;
			}
		}

		public IEnumerable<JSONNode> DeepChildren
		{
			get
			{
				foreach (var c in Children)
				{
					foreach (var dc in c.DeepChildren)
					{
						yield return dc;
					}
				}
			}
		}

		public virtual bool TryGetKey(string aKey, out JSONNode node)
		{
			node = null!;
			return false;
		}

		public virtual bool HasKey(string aKey)
		{
			return false;
		}

		public virtual JSONNode GetValueOrDefault(string aKey, JSONNode aDefault)
		{
			return aDefault;
		}

		public override string ToString()
		{
			var sb = new StringBuilder();
			WriteToStringBuilder(sb, 0, 0, JSONTextMode.Compact);
			return sb.ToString();
		}

		public virtual string ToString(int aIndent)
		{
			var sb = new StringBuilder();
			WriteToStringBuilder(sb, 0, aIndent, JSONTextMode.Indent);
			return sb.ToString();
		}

		internal abstract void WriteToStringBuilder(StringBuilder aStringBuilder, int aIndent, int aIndentInc, JSONTextMode aMode);

		public abstract Enumerator GetEnumerator();
		public IEnumerable<KeyValuePair<string, JSONNode>> Linq => new LinqEnumerator(this);
		public KeyEnumerator Keys => new KeyEnumerator(GetEnumerator());
		public ValueEnumerator Values => new ValueEnumerator(GetEnumerator());

		#endregion common interface

		#region typecasting properties

		public virtual double AsDouble
		{
			get => double.TryParse(Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0.0;
			set => Value = value.ToString(CultureInfo.InvariantCulture);
		}

		public virtual int AsInt
		{
			get {
				var doubleValue = AsDouble;
				// 检查是否超出 int 范围
				if (doubleValue > int.MaxValue || doubleValue < int.MinValue)
				{
					// 对于超出范围的值，返回 0 避免 OverflowException
					// 这主要用于处理 Bilibili API 返回的大数字 ID
					return 0;
				}
				return (int)doubleValue;
			}
			set => AsDouble = value;
		}

		public virtual float AsFloat
		{
			get => (float)AsDouble;
			set => AsDouble = value;
		}

		public virtual bool AsBool
		{
			get
			{
				if (bool.TryParse(Value, out var v))
				{
					return v;
				}

				return !string.IsNullOrEmpty(Value);
			}
			set => Value = (value) ? "true" : "false";
		}

		public virtual long AsLong
		{
			get => long.TryParse(Value, out var val) ? val : 0L;
			set => Value = value.ToString();
		}

		public virtual JSONArray? AsArray => this as JSONArray;

		public virtual JSONObject? AsObject => this as JSONObject;

		#endregion typecasting properties

		#region operators

		public static implicit operator JSONNode(string s)
		{
			return new JSONString(s);
		}

		public static implicit operator string?(JSONNode d)
		{
			return (d == null) ? null : d.Value;
		}

		public static implicit operator JSONNode(double n)
		{
			return new JSONNumber(n);
		}

		public static implicit operator double(JSONNode d)
		{
			return (d == null) ? 0 : d.AsDouble;
		}

		public static implicit operator JSONNode(float n)
		{
			return new JSONNumber(n);
		}

		public static implicit operator float(JSONNode d)
		{
			return (d == null) ? 0 : d.AsFloat;
		}

		public static implicit operator JSONNode(int n)
		{
			return new JSONNumber(n);
		}

		public static implicit operator int(JSONNode d)
		{
			return (d == null) ? 0 : d.AsInt;
		}

		public static implicit operator JSONNode(long n)
		{
			if (LongAsString)
			{
				return new JSONString(n.ToString());
			}

			return new JSONNumber(n);
		}

		public static implicit operator long(JSONNode d)
		{
			return (d == null) ? 0L : d.AsLong;
		}

		public static implicit operator JSONNode(bool b)
		{
			return new JSONBool(b);
		}

		public static implicit operator bool(JSONNode d)
		{
			return (d != null) && d.AsBool;
		}

		public static implicit operator JSONNode(KeyValuePair<string, JSONNode> aKeyValue)
		{
			return aKeyValue.Value;
		}

		public static bool operator ==(JSONNode? a, object? b)
		{
			if (ReferenceEquals(a, b))
			{
				return true;
			}

			var aIsNull = a is JSONNull || ReferenceEquals(a, null) || a is JsonLazyCreator;
			var bIsNull = b is JSONNull || ReferenceEquals(b, null) || b is JsonLazyCreator;
			if (aIsNull && bIsNull)
			{
				return true;
			}

			return !aIsNull && a!.Equals(b);
		}

		public static bool operator !=(JSONNode? a, object? b)
		{
			return !(a == b);
		}

		public override bool Equals(object? obj)
		{
			return ReferenceEquals(this, obj);
		}

		public override int GetHashCode()
		{
			return base.GetHashCode();
		}

		#endregion operators

		[ThreadStatic]
		private static StringBuilder? _mEscapeBuilder;

		internal static StringBuilder EscapeBuilder => _mEscapeBuilder ??= new StringBuilder();

		internal static string Escape(string aText)
		{
			if (string.IsNullOrEmpty(aText))
			{
				return "";
			}

			var sb = EscapeBuilder;
			sb.Length = 0;
			if (sb.Capacity < aText.Length + aText.Length / 10)
			{
				sb.Capacity = aText.Length + aText.Length / 10;
			}

			foreach (var c in aText)
			{
				switch (c)
				{
					case '\\':
						sb.Append("\\\\");
						break;
					case '\"':
						sb.Append("\\\"");
						break;
					case '\n':
						sb.Append("\\n");
						break;
					case '\r':
						sb.Append("\\r");
						break;
					case '\t':
						sb.Append("\\t");
						break;
					case '\b':
						sb.Append("\\b");
						break;
					case '\f':
						sb.Append("\\f");
						break;
					default:
						if (c < ' ' || (ForceAscii && c > 127))
						{
							ushort val = c;
							sb.Append("\\u").Append(val.ToString("X4"));
						}
						else
						{
							sb.Append(c);
						}

						break;
				}
			}

			var result = sb.ToString();
			sb.Length = 0;
			return result;
		}

		private static JSONNode ParseElement(string token, bool quoted)
		{
			if (quoted)
			{
				return token;
			}

			var tmp = token.ToLower();
			if (tmp == "false" || tmp == "true")
			{
				return tmp == "true";
			}

			if (tmp == "null")
			{
				return JSONNull.CreateOrGet();
			}

			if (double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var val))
			{
				return val;
			}
			else
			{
				return token;
			}
		}

		public static JSONNode Parse(string aJson)
		{
			var stack = new Stack<JSONNode>();
			JSONNode ctx = null!;
			var i = 0;
			var token = new StringBuilder();
			var tokenName = string.Empty;
			var quoteMode = false;
			var tokenIsQuoted = false;
			while (i < aJson.Length)
			{
				switch (aJson[i])
				{
					case '{':
						if (quoteMode)
						{
							token.Append(aJson[i]);
							break;
						}

						stack.Push(new JSONObject());
						if (ctx != null)
						{
							ctx.Add(tokenName, stack.Peek());
						}

						tokenName = string.Empty;
						token.Length = 0;
						ctx = stack.Peek();
						break;

					case '[':
						if (quoteMode)
						{
							token.Append(aJson[i]);
							break;
						}

						stack.Push(new JSONArray());
						if (ctx != null)
						{
							ctx.Add(tokenName, stack.Peek());
						}

						tokenName = "";
						token.Length = 0;
						ctx = stack.Peek();
						break;

					case '}':
					case ']':
						if (quoteMode)
						{
							token.Append(aJson[i]);
							break;
						}

						if (stack.Count == 0)
						{
							throw new Exception("JSON Parse: Too many closing brackets");
						}

						stack.Pop();
						if (token.Length > 0 || tokenIsQuoted)
						{
							ctx.Add(tokenName, ParseElement(token.ToString(), tokenIsQuoted));
						}

						tokenIsQuoted = false;
						tokenName = "";
						token.Length = 0;
						if (stack.Count > 0)
						{
							ctx = stack.Peek();
						}

						break;

					case ':':
						if (quoteMode)
						{
							token.Append(aJson[i]);
							break;
						}

						tokenName = token.ToString();
						token.Length = 0;
						tokenIsQuoted = false;
						break;

					case '"':
						quoteMode ^= true;
						tokenIsQuoted |= quoteMode;
						break;

					case ',':
						if (quoteMode)
						{
							token.Append(aJson[i]);
							break;
						}

						if (token.Length > 0 || tokenIsQuoted)
						{
							ctx.Add(tokenName, ParseElement(token.ToString(), tokenIsQuoted));
						}

						tokenName = string.Empty;
						token.Length = 0;
						tokenIsQuoted = false;
						break;

					case '\r':
					case '\n':
						break;

					case ' ':
					case '\t':
						if (quoteMode)
						{
							token.Append(aJson[i]);
						}

						break;

					case '\\':
						++i;
						if (quoteMode)
						{
							var c = aJson[i];
							switch (c)
							{
								case 't':
									token.Append('\t');
									break;
								case 'r':
									token.Append('\r');
									break;
								case 'n':
									token.Append('\n');
									break;
								case 'b':
									token.Append('\b');
									break;
								case 'f':
									token.Append('\f');
									break;
								case 'u':
									{
										var s = aJson.Substring(i + 1, 4);
										// 使用 TryParse 避免 OverflowException
										if (int.TryParse(s, NumberStyles.AllowHexSpecifier, null, out var unicodeValue))
										{
											token.Append((char)unicodeValue);
										}
										else
										{
											// 如果解析失败，添加原始的转义序列
											token.Append("\\u").Append(s);
										}
										i += 4;
										break;
									}
								default:
									token.Append(c);
									break;
							}
						}

						break;
					case '/':
						if (AllowLineComments && !quoteMode && i + 1 < aJson.Length && aJson[i + 1] == '/')
						{
							while (++i < aJson.Length && aJson[i] != '\n' && aJson[i] != '\r')
							{
							}

							break;
						}

						token.Append(aJson[i]);
						break;
					case '\uFEFF': // remove / ignore BOM (Byte Order Mark)
						break;

					default:
						token.Append(aJson[i]);
						break;
				}

				++i;
			}

			if (quoteMode)
			{
				throw new Exception("JSON Parse: Quotation marks seems to be messed up.");
			}

			if (ctx == null)
			{
				return ParseElement(token.ToString(), tokenIsQuoted);
			}

			return ctx;
		}
	}
	// End of JSONNode

	// ReSharper disable once InconsistentNaming
	public partial class JSONArray : JSONNode
	{
		private readonly List<JSONNode> _mList = new List<JSONNode>();
		private bool _inline;

		public override bool Inline
		{
			get => _inline;
			set => _inline = value;
		}

		public override JSONNodeType Tag => JSONNodeType.Array;
		public override bool IsArray => true;
		public override Enumerator GetEnumerator() { return new Enumerator(_mList.GetEnumerator()); }

		public override JSONNode this[int aIndex]
		{
			get
			{
				if (aIndex < 0 || aIndex >= _mList.Count)
				{
					return new JsonLazyCreator(this);
				}

				return _mList[aIndex];
			}
			set
			{
				if (value == null)
				{
					value = JSONNull.CreateOrGet();
				}

				if (aIndex < 0 || aIndex >= _mList.Count)
				{
					_mList.Add(value);
				}
				else
				{
					_mList[aIndex] = value;
				}
			}
		}

		public override JSONNode this[string aKey]
		{
			get => new JsonLazyCreator(this);
			set
			{
				if (value == null)
				{
					value = JSONNull.CreateOrGet();
				}

				_mList.Add(value);
			}
		}

		public override int Count => _mList.Count;

		public override void Add(string? aKey, JSONNode? aItem)
		{
			if (aItem == null)
			{
				aItem = JSONNull.CreateOrGet();
			}

			_mList.Add(aItem);
		}

		public override JSONNode? Remove(int aIndex)
		{
			if (aIndex < 0 || aIndex >= _mList.Count)
			{
				return null;
			}

			var tmp = _mList[aIndex];
			_mList.RemoveAt(aIndex);

			return tmp;
		}

		public override JSONNode Remove(JSONNode aNode)
		{
			_mList.Remove(aNode);
			return aNode;
		}

		public override JSONNode Clone()
		{
			var node = new JSONArray { _mList = { Capacity = _mList.Capacity } };
			foreach (var n in _mList)
			{
				if (n != null)
				{
					node.Add(n.Clone());
				}
				else
				{
					node.Add(null);
				}
			}

			return node;
		}

		public override IEnumerable<JSONNode> Children
		{
			get
			{
				foreach (var n in _mList)
				{
					yield return n;
				}
			}
		}

		public IReadOnlyList<JSONNode> List => _mList;


		internal override void WriteToStringBuilder(StringBuilder aStringBuilder, int aIndent, int aIndentInc, JSONTextMode aMode)
		{
			aStringBuilder.Append('[');
			var count = _mList.Count;
			if (_inline)
			{
				aMode = JSONTextMode.Compact;
			}

			for (var i = 0; i < count; i++)
			{
				if (i > 0)
				{
					aStringBuilder.Append(',');
				}

				if (aMode == JSONTextMode.Indent)
				{
					aStringBuilder.AppendLine();
				}

				if (aMode == JSONTextMode.Indent)
				{
					aStringBuilder.Append(' ', aIndent + aIndentInc);
				}

				_mList[i].WriteToStringBuilder(aStringBuilder, aIndent + aIndentInc, aIndentInc, aMode);
			}

			if (aMode == JSONTextMode.Indent)
			{
				aStringBuilder.AppendLine().Append(' ', aIndent);
			}

			aStringBuilder.Append(']');
		}

		public JSONArray() { }

		public JSONArray(IEnumerable<string> array)
		{
			foreach (var item in array)
			{
				Add(new JSONString(item));
			}
		}
	}
	// End of JSONArray

	// ReSharper disable once InconsistentNaming
	public partial class JSONObject : JSONNode
	{
		private readonly Dictionary<string, JSONNode> _mDict = new Dictionary<string, JSONNode>();

		private bool _inline;

		public override bool Inline
		{
			get => _inline;
			set => _inline = value;
		}

		public override JSONNodeType Tag => JSONNodeType.Object;
		public override bool IsObject => true;

		public override Enumerator GetEnumerator() { return new Enumerator(_mDict.GetEnumerator()); }


		public override JSONNode this[string aKey]
		{
			get
			{
				if (_mDict.ContainsKey(aKey))
				{
					return _mDict[aKey];
				}

				return new JsonLazyCreator(this, aKey);
			}
			set
			{
				if (value == null)
				{
					value = JSONNull.CreateOrGet();
				}

				if (_mDict.ContainsKey(aKey))
				{
					_mDict[aKey] = value;
				}
				else
				{
					_mDict.Add(aKey, value);
				}
			}
		}

		public override JSONNode this[int aIndex]
		{
			get
			{
				if (aIndex < 0 || aIndex >= _mDict.Count)
				{
					return null!;
				}

				return _mDict.ElementAt(aIndex).Value;
			}
			set
			{
				if (value == null)
				{
					value = JSONNull.CreateOrGet();
				}

				if (aIndex < 0 || aIndex >= _mDict.Count)
				{
					return;
				}

				var key = _mDict.ElementAt(aIndex).Key;
				_mDict[key] = value;
			}
		}

		public override int Count => _mDict.Count;

		public override void Add(string? aKey, JSONNode? aItem)
		{
			if (aItem == null)
			{
				aItem = JSONNull.CreateOrGet();
			}

			if (aKey != null)
			{
				if (_mDict.ContainsKey(aKey))
				{
					_mDict[aKey] = aItem;
				}
				else
				{
					_mDict.Add(aKey, aItem);
				}
			}
			else
			{
				_mDict.Add(Guid.NewGuid().ToString(), aItem);
			}
		}

		public override JSONNode? Remove(string aKey)
		{
			if (!_mDict.ContainsKey(aKey))
			{
				return null;
			}

			var tmp = _mDict[aKey];
			_mDict.Remove(aKey);

			return tmp;
		}

		public override JSONNode? Remove(int aIndex)
		{
			if (aIndex < 0 || aIndex >= _mDict.Count)
			{
				return null;
			}

			var item = _mDict.ElementAt(aIndex);
			_mDict.Remove(item.Key);

			return item.Value;
		}

		public override JSONNode? Remove(JSONNode aNode)
		{
			try
			{
				var item = _mDict.Where(k => k.Value == aNode).First();
				_mDict.Remove(item.Key);
				return aNode;
			}
			catch
			{
				return null;
			}
		}

		public override JSONNode Clone()
		{
			var node = new JSONObject();
			foreach (var n in _mDict)
			{
				node.Add(n.Key, n.Value.Clone());
			}

			return node;
		}

		public override bool HasKey(string aKey)
		{
			return _mDict.ContainsKey(aKey);
		}

		public override bool TryGetKey(string aKey, out JSONNode node)
		{
			return _mDict.TryGetValue(aKey, out node);
		}


		public override JSONNode GetValueOrDefault(string aKey, JSONNode aDefault)
		{
			if (_mDict.TryGetValue(aKey, out var res))
			{
				return res;
			}

			return aDefault;
		}

		public override IEnumerable<JSONNode> Children
		{
			get
			{
				foreach (var n in _mDict)
				{
					yield return n.Value;
				}
			}
		}

		internal override void WriteToStringBuilder(StringBuilder aStringBuilder, int aIndent, int aIndentInc, JSONTextMode aMode)
		{
			aStringBuilder.Append('{');
			var first = true;
			if (_inline)
			{
				aMode = JSONTextMode.Compact;
			}

			foreach (var k in _mDict)
			{
				if (!first)
				{
					aStringBuilder.Append(',');
				}

				first = false;
				if (aMode == JSONTextMode.Indent)
				{
					aStringBuilder.AppendLine();
				}

				if (aMode == JSONTextMode.Indent)
				{
					aStringBuilder.Append(' ', aIndent + aIndentInc);
				}

				aStringBuilder.Append('\"').Append(Escape(k.Key)).Append('\"');
				if (aMode == JSONTextMode.Compact)
				{
					aStringBuilder.Append(':');
				}
				else
				{
					aStringBuilder.Append(" : ");
				}

				k.Value.WriteToStringBuilder(aStringBuilder, aIndent + aIndentInc, aIndentInc, aMode);
			}

			if (aMode == JSONTextMode.Indent)
			{
				aStringBuilder.AppendLine().Append(' ', aIndent);
			}

			aStringBuilder.Append('}');
		}
	}
	// End of JSONObject

	// ReSharper disable once InconsistentNaming
	public partial class JSONString : JSONNode
	{
		private string _mData;

		public override JSONNodeType Tag => JSONNodeType.String;
		public override bool IsString => true;

		public override Enumerator GetEnumerator() { return new Enumerator(); }


		public override string Value
		{
			get => _mData;
			set => _mData = value;
		}

		public JSONString(string aData)
		{
			_mData = aData;
		}

		public override JSONNode Clone()
		{
			return new JSONString(_mData);
		}

		internal override void WriteToStringBuilder(StringBuilder aStringBuilder, int aIndent, int aIndentInc, JSONTextMode aMode)
		{
			aStringBuilder.Append('\"').Append(Escape(_mData)).Append('\"');
		}

		public override bool Equals(object? obj)
		{
			if (base.Equals(obj))
			{
				return true;
			}

			if (obj is string s)
			{
				return _mData == s;
			}

			var s2 = obj as JSONString;
			if (s2 != null)
			{
				return _mData == s2._mData;
			}

			return false;
		}

		public override int GetHashCode()
		{
			return _mData.GetHashCode();
		}
	}
	// End of JSONString

	// ReSharper disable once InconsistentNaming
	public partial class JSONNumber : JSONNode
	{
		private double _mData;

		public override JSONNodeType Tag => JSONNodeType.Number;
		public override bool IsNumber => true;
		public override Enumerator GetEnumerator() { return new Enumerator(); }

		public override string Value
		{
			get => _mData.ToString(CultureInfo.InvariantCulture);
			set
			{
				if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
				{
					_mData = v;
				}
			}
		}

		public override double AsDouble
		{
			get => _mData;
			set => _mData = value;
		}

		public override long AsLong
		{
			get => (long)_mData;
			set => _mData = value;
		}

		public JSONNumber(double aData)
		{
			_mData = aData;
		}

		public JSONNumber(string aData)
		{
			Value = aData;
		}

		public override JSONNode Clone()
		{
			return new JSONNumber(_mData);
		}

		internal override void WriteToStringBuilder(StringBuilder aStringBuilder, int aIndent, int aIndentInc, JSONTextMode aMode)
		{
			aStringBuilder.Append(Value);
		}

		private static bool IsNumeric(object value)
		{
			return value is int || value is uint
								|| value is float || value is double
								|| value is decimal
								|| value is long || value is ulong
								|| value is short || value is ushort
								|| value is sbyte || value is byte;
		}

		public override bool Equals(object? obj)
		{
			if (obj == null)
			{
				return false;
			}

			if (base.Equals(obj))
			{
				return true;
			}

			var s2 = obj as JSONNumber;
			if (s2 != null)
			{
				return Math.Abs(_mData - s2._mData) < 0.001f;
			}

			if (IsNumeric(obj))
			{
				return Math.Abs(Convert.ToDouble(obj) - _mData) < 0.001f;
			}

			return false;
		}

		public override int GetHashCode()
		{
			return _mData.GetHashCode();
		}
	}
	// End of JSONNumber

	// ReSharper disable once InconsistentNaming
	public partial class JSONBool : JSONNode
	{
		private bool _mData;

		public override JSONNodeType Tag => JSONNodeType.Boolean;
		public override bool IsBoolean => true;
		public override Enumerator GetEnumerator() { return new Enumerator(); }

		public override string Value
		{
			get => _mData.ToString();
			set
			{
				if (bool.TryParse(value, out var v))
				{
					_mData = v;
				}
			}
		}

		public override bool AsBool
		{
			get => _mData;
			set => _mData = value;
		}

		public JSONBool(bool aData)
		{
			_mData = aData;
		}

		public JSONBool(string aData)
		{
			Value = aData;
		}

		public override JSONNode Clone()
		{
			return new JSONBool(_mData);
		}

		internal override void WriteToStringBuilder(StringBuilder aStringBuilder, int aIndent, int aIndentInc, JSONTextMode aMode)
		{
			aStringBuilder.Append((_mData) ? "true" : "false");
		}

		public override bool Equals(object? obj)
		{
			return obj switch
			{
				null => false,
				bool b => _mData == b,
				_ => false
			};
		}

		public override int GetHashCode()
		{
			return _mData.GetHashCode();
		}
	}
	// End of JSONBool

	// ReSharper disable once InconsistentNaming
	public partial class JSONNull : JSONNode
	{
		private static readonly JSONNull MStaticInstance = new JSONNull();
		public static bool ReuseSameInstance = true;

		public static JSONNull CreateOrGet()
		{
			return ReuseSameInstance ? MStaticInstance : new JSONNull();
		}

		private JSONNull() { }

		public override JSONNodeType Tag => JSONNodeType.NullValue;
		public override bool IsNull => true;
		public override Enumerator GetEnumerator() { return new Enumerator(); }

		public override string Value
		{
			get => "null";
			set { }
		}

		public override bool AsBool
		{
			get => false;
			set { }
		}

		public override JSONNode Clone()
		{
			return CreateOrGet();
		}

		public override bool Equals(object? obj)
		{
			if (ReferenceEquals(this, obj))
			{
				return true;
			}

			return (obj is JSONNull);
		}

		public override int GetHashCode()
		{
			return 0;
		}

		internal override void WriteToStringBuilder(StringBuilder aStringBuilder, int aIndent, int aIndentInc, JSONTextMode aMode)
		{
			aStringBuilder.Append("null");
		}
	}
	// End of JSONNull

	internal class JsonLazyCreator : JSONNode
	{
		private readonly string? _mKey;

		private JSONNode _mNode;

		public JsonLazyCreator(JSONNode aNode)
		{
			_mNode = aNode;
			_mKey = null;
		}

		public JsonLazyCreator(JSONNode aNode, string aKey)
		{
			_mNode = aNode;
			_mKey = aKey;
		}

		public override JSONNodeType Tag => JSONNodeType.None;
		public override Enumerator GetEnumerator() { return new Enumerator(); }

		private T Set<T>(T aVal) where T : JSONNode
		{
			if (_mKey == null)
			{
				_mNode.Add(aVal);
			}
			else
			{
				_mNode.Add(_mKey, aVal);
			}

			_mNode = null!; // Be GC friendly.
			return aVal;
		}

		public override JSONNode this[int aIndex]
		{
			get => new JsonLazyCreator(this);
			set => Set(new JSONArray()).Add(value);
		}

		public override JSONNode this[string aKey]
		{
			get => new JsonLazyCreator(this, aKey);
			set => Set(new JSONObject()).Add(aKey, value);
		}

		public override void Add(JSONNode? anItem)
		{
			Set(new JSONArray()).Add(anItem);
		}

		public override void Add(string? aKey, JSONNode? aItem)
		{
			Set(new JSONObject()).Add(aKey, aItem);
		}

		public static bool operator ==(JsonLazyCreator a, object? b)
		{
			return b == null || ReferenceEquals(a, b);
		}

		public static bool operator !=(JsonLazyCreator a, object b)
		{
			return !(a == b);
		}

		public override bool Equals(object? obj)
		{
			return obj == null || ReferenceEquals(this, obj);
		}

		public override int GetHashCode()
		{
			return 0;
		}

		public override int AsInt
		{
			get
			{
				Set(new JSONNumber(0));
				return 0;
			}
			set => Set(new JSONNumber(value));
		}

		public override float AsFloat
		{
			get
			{
				Set(new JSONNumber(0.0f));
				return 0.0f;
			}
			set => Set(new JSONNumber(value));
		}

		public override double AsDouble
		{
			get
			{
				Set(new JSONNumber(0.0));
				return 0.0;
			}
			set => Set(new JSONNumber(value));
		}

		public override long AsLong
		{
			get
			{
				if (LongAsString)
				{
					Set(new JSONString("0"));
				}
				else
				{
					Set(new JSONNumber(0.0));
				}

				return 0L;
			}
			set
			{
				if (LongAsString)
				{
					Set(new JSONString(value.ToString()));
				}
				else
				{
					Set(new JSONNumber(value));
				}
			}
		}

		public override bool AsBool
		{
			get
			{
				Set(new JSONBool(false));
				return false;
			}
			set => Set(new JSONBool(value));
		}

		public override JSONArray AsArray => Set(new JSONArray());

		public override JSONObject AsObject => Set(new JSONObject());

		internal override void WriteToStringBuilder(StringBuilder aStringBuilder, int aIndent, int aIndentInc, JSONTextMode aMode)
		{
			aStringBuilder.Append("null");
		}
	}
	// End of JSONLazyCreator

	// ReSharper disable once InconsistentNaming
	public static class JSON
	{
		public static JSONNode Parse(string aJson)
		{
			return JSONNode.Parse(aJson);
		}
	}
}