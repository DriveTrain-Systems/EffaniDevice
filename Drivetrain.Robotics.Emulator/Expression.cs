using System.Globalization;

namespace Drivetrain.Robotics.Emulator
{
	/// <summary>
	/// Evaluates the expressions RepRapFirmware allows in G-code (in <c>if</c>, <c>echo</c> and <c>{...}</c> parameters), as far as the
	/// emulator needs them: numbers, strings, true/false/null, object model values (move.axes[2].homed), comparisons, &amp;&amp; || !,
	/// + - * / and brackets. A value that cannot be worked out throws a <see cref="FormatException"/> saying why.
	/// </summary>
	internal sealed class Expression
	{
		private readonly string _text;
		private readonly Func<string, object?> _objectModel;
		private int _at;

		private Expression(string text, Func<string, object?> objectModel)
		{
			_text = text;
			_objectModel = objectModel;
		}
		/// <param name="objectModel">Returns the value of an object model path, or throws when there is no such value.</param>
		public static object? Evaluate(string text, Func<string, object?> objectModel)
		{
			var expression = new Expression(text, objectModel);
			var value = expression.Or();
			expression.Space();
			if (expression._at < text.Length)
				throw new FormatException($"unexpected '{text.Substring(expression._at)}'");
			return value;
		}
		public static bool Truth(object? value) => value switch
		{
			bool b => b,
			null => false,
			double d => d != 0,
			_ => throw new FormatException("expected a true or false value"),
		};
		public static string Text(object? value) => value switch
		{
			null => "null",
			bool b => b ? "true" : "false",
			double d => d.ToString("0.###", CultureInfo.InvariantCulture),
			_ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "",
		};

		private void Space()
		{
			while (_at < _text.Length && char.IsWhiteSpace(_text[_at]))
				_at++;
		}
		private bool Take(string symbol)
		{
			Space();
			if (string.CompareOrdinal(_text, _at, symbol, 0, symbol.Length) != 0)
				return false;
			_at += symbol.Length;
			return true;
		}
		private object? Or()
		{
			var left = And();
			while (Take("||"))
			{
				var right = And();
				left = Truth(left) || Truth(right);
			}
			return left;
		}
		private object? And()
		{
			var left = Compare();
			while (Take("&&"))
			{
				var right = Compare();
				left = Truth(left) && Truth(right);
			}
			return left;
		}
		private object? Compare()
		{
			var left = Sum();
			foreach (var symbol in new[] { "==", "!=", "<=", ">=", "<", ">", "=" })
			{
				if (!Take(symbol))
					continue;
				var right = Sum();
				if (symbol is "==" or "=" or "!=")
				{
					var same = left is double a && right is double b ? a == b : Equals(left, right);
					return symbol == "!=" ? !same : same;
				}
				var x = Number(left);
				var y = Number(right);
				return symbol switch { "<=" => x <= y, ">=" => x >= y, "<" => x < y, _ => x > y };
			}
			return left;
		}
		private object? Sum()
		{
			var left = Product();
			while (true)
			{
				if (Take("+"))
					left = Number(left) + Number(Product());
				else if (Take("-"))
					left = Number(left) - Number(Product());
				else if (Take("^"))
					left = Text(left) + Text(Product());
				else
					return left;
			}
		}
		private object? Product()
		{
			var left = Unary();
			while (true)
			{
				if (Take("*"))
					left = Number(left) * Number(Unary());
				else if (Take("/"))
					left = Number(left) / Number(Unary());
				else
					return left;
			}
		}
		private object? Unary()
		{
			if (Take("!"))
				return !Truth(Unary());
			if (Take("-"))
				return -Number(Unary());
			if (Take("("))
			{
				var inner = Or();
				if (!Take(")"))
					throw new FormatException("missing ')'");
				return inner;
			}
			if (Take("{"))
			{
				var inner = Or();
				if (!Take("}"))
					throw new FormatException("missing '}'");
				return inner;
			}
			Space();
			if (_at >= _text.Length)
				throw new FormatException("the expression is incomplete");
			var c = _text[_at];
			if (c == '"')
			{
				var text = new System.Text.StringBuilder();
				for (_at++; _at < _text.Length; _at++)
				{
					if (_text[_at] == '"')
					{
						if (_at + 1 < _text.Length && _text[_at + 1] == '"')
						{
							text.Append('"');
							_at++;
							continue;
						}
						_at++;
						return text.ToString();
					}
					text.Append(_text[_at]);
				}
				throw new FormatException("a string has no closing quote");
			}
			if (char.IsDigit(c) || c == '.')
			{
				var start = _at;
				while (_at < _text.Length && (char.IsDigit(_text[_at]) || _text[_at] == '.'))
					_at++;
				return double.Parse(_text.AsSpan(start, _at - start), NumberStyles.Float, CultureInfo.InvariantCulture);
			}
			if (char.IsLetter(c))
			{
				var start = _at;
				while (_at < _text.Length && (char.IsLetterOrDigit(_text[_at]) || _text[_at] is '.' or '[' or ']' or '_'))
					_at++;
				var name = _text.Substring(start, _at - start);
				return name switch
				{
					"true" => true,
					"false" => false,
					"null" => null,
					_ => _objectModel(name),
				};
			}
			throw new FormatException($"unexpected '{c}'");
		}
		private static double Number(object? value) => value switch
		{
			double d => d,
			bool => throw new FormatException("expected a number, not true or false"),
			null => throw new FormatException("expected a number, not null"),
			_ => double.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : throw new FormatException("expected a number"),
		};
	}
}
