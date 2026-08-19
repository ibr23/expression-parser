/*
 * This file is part of an MIT-licensed project: see LICENSE file or README.md for details.
 * Copyright (c) 2025 Ian Thomas
 */

using System.Reflection;
using System.Text.RegularExpressions;
using System.Globalization;

namespace ExpressionParser
{
    // Utility functions: type conversion and formatting
    public static class Utils
    {
        public static bool MakeBool(object val)
        {
            if (val is bool b)
                return b;
            if (val is int i)
                return i != 0;
            if (val is double d)
                return d != 0;
            if (val is string s)
            {
                string lower = s.ToLower();
                return lower == "true" || lower == "1";
            }
            throw new Exception($"Type mismatch: Expecting bool, but got '{val}'");
        }

        public static double MakeNumeric(object val)
        {
            if (val is bool b)
                return b ? 1 : 0;
            if (val is int i)
                return Convert.ToDouble(i);
            if (val is double d)
                return d;
            if (val is string s)
            {
                // Parsing always uses '.' as the decimal point, same as
                // expression literals -- Writer.DecimalSeparator only affects
                // formatting/display, not parsing. InvariantCulture keeps this
                // from depending on the current thread's culture either.
                if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double dResult))
                    return dResult;
            }
            throw new Exception($"Type mismatch: Expecting number but got '{val}'");
        }

        public static string MakeString(object val)
        {
            if (val is string s)
                return s;
            if (val is bool b)
                return b ? "true" : "false";
            if (val is int i)
                return i.ToString();
            if (val is double d)
                return FormatNumeric(d);
            throw new Exception($"Type mismatch: Expecting string but got '{val}'");
        }

        public static object MakeTypeMatch(object leftVal, object rightVal)
        {
            if (leftVal is bool)
                return MakeBool(rightVal);
            if (leftVal is int || leftVal is double)
                return MakeNumeric(rightVal);
            if (leftVal is string)
                return MakeString(rightVal);
            throw new Exception($"Type mismatch: unrecognised type for '{leftVal}'");
        }

        // Numeric types compare by value regardless of whether they're boxed
        // as int or double (MakeTypeMatch always coerces the right-hand side
        // of a comparison to double, so object.Equals's strict boxed-type
        // check would make e.g. an int context variable never equal a
        // numeric literal).
        public static bool ObjectEquals(object a, object b)
        {
            bool aNumeric = (a is int || a is double);
            bool bNumeric = (b is int || b is double);
            if (aNumeric && bNumeric)
                return MakeNumeric(a) == MakeNumeric(b);

            return a.Equals(b);
        }

        public static string FormatBoolean(bool val) => val ? "true" : "false";

        public static string FormatNumeric(double num)
        {
            // Always format with InvariantCulture so this doesn't drift with
            // the current thread's culture; the configured decimal separator
            // is applied afterwards instead.
            string s;
            if (num % 1 == 0)
                s = ((int)num).ToString(CultureInfo.InvariantCulture);
            else
                s = num.ToString(CultureInfo.InvariantCulture);

            char sep = Writer.DecimalSeparator;
            return sep != '.' ? s.Replace('.', sep) : s;
        }

        public static string FormatString(string val)
        {
            switch (Writer.StringFormat)
            {
                case Writer.STRING_FORMAT.SINGLEQUOTE:
                    return $"'{val}'";
                case Writer.STRING_FORMAT.ESCAPED_SINGLEQUOTE:
                    return $"\\'{val}\\'";
                case Writer.STRING_FORMAT.ESCAPED_DOUBLEQUOTE:
                    return $"\\\"{val}\\\"";
                case Writer.STRING_FORMAT.DOUBLEQUOTE:
                default:
                    return $"\"{val}\"";
            }
        }

        public static string FormatValue(object val)
        {
            if (val is bool b)
                return FormatBoolean(b);
            if (val is int i)
                return i.ToString();
            if (val is double d)
                return FormatNumeric(d);
            if (val is string s)
                return FormatString(s);
            return "";
        }

        // fmt-style string formatting. Placeholders look like {index}, {index,width},
        // {index:precision}, or {index,width:precision}:
        //   index     - 0-based, selects args[index].
        //   width     - signed; positive right-aligns (pads left), negative
        //               left-aligns (pads right), to abs(width) characters.
        //   precision - decimal places; only applies when the arg is int/double,
        //               ignored otherwise.
        // Literal braces are written as {{ and }}. Values are stringified via
        // MakeString (unquoted), not FormatValue.
        private static readonly Regex FormatSpecRegex = new Regex(@"^(\d+)(?:,(-?\d+))?(?::(\d+))?$");

        public static string Format(string fmtStr, object[] args)
        {
            var result = new System.Text.StringBuilder();
            int i = 0;
            while (i < fmtStr.Length)
            {
                char c = fmtStr[i];
                if (c == '{')
                {
                    if (i + 1 < fmtStr.Length && fmtStr[i + 1] == '{')
                    {
                        result.Append('{');
                        i += 2;
                        continue;
                    }
                    int end = fmtStr.IndexOf('}', i + 1);
                    if (end == -1)
                        throw new Exception("Unmatched '{' in format string.");
                    string spec = fmtStr.Substring(i + 1, end - i - 1);
                    Match m = FormatSpecRegex.Match(spec);
                    if (!m.Success)
                        throw new Exception($"Invalid format placeholder '{{{spec}}}'.");

                    int index = int.Parse(m.Groups[1].Value);
                    bool hasWidth = m.Groups[2].Success;
                    int width = hasWidth ? int.Parse(m.Groups[2].Value) : 0;
                    bool hasPrecision = m.Groups[3].Success;
                    int precision = hasPrecision ? int.Parse(m.Groups[3].Value) : 0;

                    if (index < 0 || index >= args.Length)
                        throw new Exception($"Format index {index} out of range.");
                    object val = args[index];

                    string valStr;
                    if (hasPrecision && (val is int || val is double))
                    {
                        valStr = MakeNumeric(val).ToString("F" + precision, CultureInfo.InvariantCulture);
                        char sep = Writer.DecimalSeparator;
                        if (sep != '.')
                            valStr = valStr.Replace('.', sep);
                    }
                    else
                        valStr = MakeString(val);

                    if (hasWidth)
                    {
                        int absWidth = Math.Abs(width);
                        if (valStr.Length < absWidth)
                        {
                            string pad = new string(' ', absWidth - valStr.Length);
                            valStr = width < 0 ? valStr + pad : pad + valStr;
                        }
                    }

                    result.Append(valStr);
                    i = end + 1;
                    continue;
                }
                if (c == '}')
                {
                    if (i + 1 < fmtStr.Length && fmtStr[i + 1] == '}')
                    {
                        result.Append('}');
                        i += 2;
                        continue;
                    }
                    throw new Exception("Unmatched '}' in format string.");
                }
                result.Append(c);
                i++;
            }
            return result.ToString();
        }

        // Convenience for registering as a context function, e.g.
        // context["format"] = new Func<object[], object>(Utils.FormatFunction);
        // Usage: format("{0} is {1:2} years old", name, age)
        public static object FormatFunction(object[] args)
        {
            if (args.Length == 0)
                throw new Exception("format requires at least a format string argument.");
            string fmtStr = MakeString(args[0]);
            object[] rest = new object[args.Length - 1];
            Array.Copy(args, 1, rest, 0, rest.Length);
            return Format(fmtStr, rest);
        }
    }

    public abstract class ExpressionNode
    {
        public string Name { get; set; }
        public int Precedence { get; set; }

        public int Specificity{ get {return _specificity;}}

        protected int _specificity = 0;

        protected ExpressionNode(string name, int precedence)
        {
            Name = name;
            Precedence = precedence;
        }

        public abstract object Evaluate(Dictionary<string, object> context, List<string>? dumpEval = null);
        public abstract string DumpStructure(int indent = 0);
        public abstract string Write();
    }

    // Abstract base for binary operators
    public abstract class BinaryOp : ExpressionNode
    {
        protected ExpressionNode Left;
        protected ExpressionNode Right;
        protected string Op;

        protected BinaryOp(string name, ExpressionNode left, string op, ExpressionNode right, int precedence)
            : base(name, precedence)
        {
            Left = left;
            Op = op;
            Right = right;
            _specificity = left.Specificity + right.Specificity;
        }

        public override object Evaluate(Dictionary<string, object> context, List<string>? dumpEval = null)
        {
            object leftVal = Left.Evaluate(context, dumpEval);
            var (shortCircuit, shortCircuitResult) = this.ShortCircuit(leftVal);
            if (shortCircuit)
            {
                if (dumpEval != null)
                {
                    dumpEval.Add($"Evaluated: {Utils.FormatValue(leftVal)} {Op} (ignore) = {Utils.FormatValue(shortCircuitResult!)}");
                }
                return shortCircuitResult!;
            }
            object rightVal = Right.Evaluate(context, dumpEval);
            object result = DoEval(leftVal, rightVal);
            if (dumpEval != null)
            {
                dumpEval.Add($"Evaluated: {Utils.FormatValue(leftVal)} {Op} {Utils.FormatValue(rightVal)} = {Utils.FormatValue(result)}");
            }
            return result;
        }

        protected abstract object DoEval(object leftVal, object rightVal);

        protected virtual (bool shortCircuit, object? shortCircuitResult) ShortCircuit(object leftVal)
        {
            return (false, null);
        }

        public override string DumpStructure(int indent = 0)
        {
            string indentStr = new string(' ', indent * 2);
            string outStr = indentStr + Name + "\n" +
                            Left.DumpStructure(indent + 1) +
                            Right.DumpStructure(indent + 1);
            return outStr;
        }

        public override string Write()
        {
            string leftStr = Left.Write();
            string rightStr = Right.Write();

            if (Left.Precedence < this.Precedence)
                leftStr = "(" + leftStr + ")";
            if (Right.Precedence < this.Precedence)
                rightStr = "(" + rightStr + ")";

            return $"{leftStr} {Op} {rightStr}";
        }
    }

    // Concrete binary operator nodes
    public class OpOr : BinaryOp
    {
        public OpOr(ExpressionNode left, ExpressionNode right)
            : base("Or", left, "or", right, 40) 
        { 
            _specificity +=1;
        }

        protected override (bool shortCircuit, object? shortCircuitResult) ShortCircuit(object leftVal)
        {
            bool result = Utils.MakeBool(leftVal);
            if (result)
                return (true, true);
            return (false, null);
        }
        protected override object DoEval(object leftVal, object rightVal)
        {
            return Utils.MakeBool(leftVal) || Utils.MakeBool(rightVal);
        }
    }

    public class OpAnd : BinaryOp
    {
        public OpAnd(ExpressionNode left, ExpressionNode right)
            : base("And", left, "and", right, 50)
        { 
            _specificity +=1;
        }

        protected override (bool shortCircuit, object? shortCircuitResult) ShortCircuit(object leftVal)
        {
            bool result = Utils.MakeBool(leftVal);
            if (!result)
                return (true, false);
            return (false, null);
        }
        protected override object DoEval(object leftVal, object rightVal)
        {
            return Utils.MakeBool(leftVal) && Utils.MakeBool(rightVal);
        }
    }

    public class OpEquals : BinaryOp
    {
        public OpEquals(ExpressionNode left, ExpressionNode right)
            : base("Equals", left, "==", right, 60) { }

        protected override object DoEval(object leftVal, object rightVal)
        {
            rightVal = Utils.MakeTypeMatch(leftVal, rightVal);
            return Utils.ObjectEquals(leftVal, rightVal);
        }
    }

    public class OpNotEquals : BinaryOp
    {
        public OpNotEquals(ExpressionNode left, ExpressionNode right)
            : base("NotEquals", left, "!=", right, 60) { }

        protected override object DoEval(object leftVal, object rightVal)
        {
            rightVal = Utils.MakeTypeMatch(leftVal, rightVal);
            return !Utils.ObjectEquals(leftVal, rightVal);
        }
    }

    public class OpPlus : BinaryOp
    {
        public OpPlus(ExpressionNode left, ExpressionNode right)
            : base("Plus", left, "+", right, 70) { }

        protected override object DoEval(object leftVal, object rightVal)
        {
            return Utils.MakeNumeric(leftVal) + Utils.MakeNumeric(rightVal);
        }
    }

    public class OpMinus : BinaryOp
    {
        public OpMinus(ExpressionNode left, ExpressionNode right)
            : base("Minus", left, "-", right, 70) { }

        protected override object DoEval(object leftVal, object rightVal)
        {
            return Utils.MakeNumeric(leftVal) - Utils.MakeNumeric(rightVal);
        }
    }

    public class OpDivide : BinaryOp
    {
        public OpDivide(ExpressionNode left, ExpressionNode right)
            : base("Divide", left, "/", right, 85) { }

        protected override object DoEval(object leftVal, object rightVal)
        {
            double numRight = Utils.MakeNumeric(rightVal);
            if (numRight == 0)
                throw new DivideByZeroException("Division by zero.");
            return Utils.MakeNumeric(leftVal) / numRight;
        }
    }

    public class OpMultiply : BinaryOp
    {
        public OpMultiply(ExpressionNode left, ExpressionNode right)
            : base("Multiply", left, "*", right, 80) { }
        protected override (bool shortCircuit, object? shortCircuitResult) ShortCircuit(object leftVal)
        {
            double result = Utils.MakeNumeric(leftVal);
            if (result==0.0)
                return (true, 0.0);
            return (false, null);
        }
        protected override object DoEval(object leftVal, object rightVal)
        {
            return Utils.MakeNumeric(leftVal) * Utils.MakeNumeric(rightVal);
        }
    }

    public class OpGreaterThan : BinaryOp
    {
        public OpGreaterThan(ExpressionNode left, ExpressionNode right)
            : base("GreaterThan", left, ">", right, 60) { }

        protected override object DoEval(object leftVal, object rightVal)
        {
            return Utils.MakeNumeric(leftVal) > Utils.MakeNumeric(rightVal);
        }
    }

    public class OpLessThan : BinaryOp
    {
        public OpLessThan(ExpressionNode left, ExpressionNode right)
            : base("LessThan", left, "<", right, 60) { }

        protected override object DoEval(object leftVal, object rightVal)
        {
            return Utils.MakeNumeric(leftVal) < Utils.MakeNumeric(rightVal);
        }
    }

    public class OpGreaterThanEquals : BinaryOp
    {
        public OpGreaterThanEquals(ExpressionNode left, ExpressionNode right)
            : base("GreaterThanEquals", left, ">=", right, 60) { }

        protected override object DoEval(object leftVal, object rightVal)
        {
            return Utils.MakeNumeric(leftVal) >= Utils.MakeNumeric(rightVal);
        }
    }

    public class OpLessThanEquals : BinaryOp
    {
        public OpLessThanEquals(ExpressionNode left, ExpressionNode right)
            : base("LessThanEquals", left, "<=", right, 60) { }

        protected override object DoEval(object leftVal, object rightVal)
        {
            return Utils.MakeNumeric(leftVal) <= Utils.MakeNumeric(rightVal);
        }
    }

    // String concatenation: left .. right. Both operands are coerced to string
    // regardless of type (numbers/booleans are formatted, not added numerically).
    public class OpConcat : BinaryOp
    {
        public OpConcat(ExpressionNode left, ExpressionNode right)
            : base("Concat", left, "..", right, 65) { }

        protected override object DoEval(object leftVal, object rightVal)
        {
            return Utils.MakeString(leftVal) + Utils.MakeString(rightVal);
        }
    }

    // Ternary conditional operator: condition ? trueExpr : falseExpr
    public class OpTernary : ExpressionNode
    {
        private ExpressionNode _condition;
        private ExpressionNode _trueExpr;
        private ExpressionNode _falseExpr;

        public OpTernary(ExpressionNode condition, ExpressionNode trueExpr, ExpressionNode falseExpr)
            : base("Ternary", 30)
        {
            _condition = condition;
            _trueExpr = trueExpr;
            _falseExpr = falseExpr;
            _specificity = condition.Specificity + trueExpr.Specificity + falseExpr.Specificity;
        }

        public override object Evaluate(Dictionary<string, object> context, List<string>? dumpEval = null)
        {
            object condVal = _condition.Evaluate(context, dumpEval);
            bool cond = Utils.MakeBool(condVal);
            object result = cond
                ? _trueExpr.Evaluate(context, dumpEval)
                : _falseExpr.Evaluate(context, dumpEval);

            if (dumpEval != null)
            {
                dumpEval.Add($"Evaluated: {Utils.FormatValue(condVal)} ? {(cond ? "..." : "(skipped)")} : {(cond ? "(skipped)" : "...")} = {Utils.FormatValue(result)}");
            }
            return result;
        }

        public override string DumpStructure(int indent = 0)
        {
            string indentStr = new string(' ', indent * 2);
            return indentStr + "Ternary\n" +
                   _condition.DumpStructure(indent + 1) +
                   _trueExpr.DumpStructure(indent + 1) +
                   _falseExpr.DumpStructure(indent + 1);
        }

        public override string Write()
        {
            string condStr = _condition.Write();
            string trueStr = _trueExpr.Write();
            string falseStr = _falseExpr.Write();

            if (_condition.Precedence < this.Precedence)
                condStr = "(" + condStr + ")";
            if (_trueExpr.Precedence < this.Precedence)
                trueStr = "(" + trueStr + ")";
            if (_falseExpr.Precedence < this.Precedence)
                falseStr = "(" + falseStr + ")";

            return $"{condStr} ? {trueStr} : {falseStr}";
        }
    }

    // Abstract base for unary operators
    public abstract class UnaryOp : ExpressionNode
    {
        protected ExpressionNode Operand;
        protected string Op;

        protected UnaryOp(string name, string op, ExpressionNode operand, int precedence)
            : base(name, precedence)
        {
            Operand = operand;
            Op = op;
            _specificity = operand.Specificity;
        }

        public override object Evaluate(Dictionary<string, object> context, List<string>? dumpEval = null)
        {
            object val = Operand.Evaluate(context, dumpEval);
            object result = DoEval(val);
            if (dumpEval != null)
            {
                dumpEval.Add($"Evaluated: {Op} {Utils.FormatValue(val)} = {Utils.FormatValue(result)}");
            }
            return result;
        }

        protected abstract object DoEval(object val);

        public override string DumpStructure(int indent = 0)
        {
            string indentStr = new string(' ', indent * 2);
            return indentStr + Name + "\n" + Operand.DumpStructure(indent + 1);
        }

        public override string Write()
        {
            string operandStr = Operand.Write();
            if (Operand.Precedence < this.Precedence)
                operandStr = "(" + operandStr + ")";
            return $"{Op} {operandStr}";
        }
    }

    public class OpNegative : UnaryOp
    {
        public OpNegative(ExpressionNode operand)
            : base("Negative", "-", operand, 90) { }

        protected override object DoEval(object val)
        {
            double num = Utils.MakeNumeric(val);
            return -num;
        }
    }

    public class OpNot : UnaryOp
    {
        public OpNot(ExpressionNode operand)
            : base("Not", "not", operand, 90) { }

        protected override object DoEval(object val)
        {
            bool b = Utils.MakeBool(val);
            return !b;
        }
    }

    // Literal nodes
    public class LiteralBoolean : ExpressionNode
    {
        private bool _value;
        public LiteralBoolean(bool value) : base("Boolean", 100)
        {
            _value = value;
        }

        public override object Evaluate(Dictionary<string, object> context, List<string>? dumpEval = null)
        {
            if (dumpEval != null)
                dumpEval.Add($"Boolean: {Utils.FormatBoolean(_value)}");
            return _value;
        }

        public override string DumpStructure(int indent = 0)
        {
            string indentStr = new string(' ', indent * 2);
            return indentStr + $"Boolean({Utils.FormatBoolean(_value)})\n";
        }

        public override string Write() => Utils.FormatBoolean(_value);
    }

    public class LiteralNumber : ExpressionNode
    {
        private double _value;
        public LiteralNumber(string value) : base("Number", 100)
        {
            // Numeric literals in expression source are always '.'-decimal
            // (fixed by the tokenizer's grammar, independent of
            // Writer.DecimalSeparator). InvariantCulture keeps this from
            // being thrown off by the current thread's culture either.
            _value = double.Parse(value, CultureInfo.InvariantCulture);
        }

        public override object Evaluate(Dictionary<string, object> context, List<string>? dumpEval = null)
        {
            if (dumpEval != null)
                dumpEval.Add($"Number: {Utils.FormatNumeric(_value)}");
            return _value;
        }

        public override string DumpStructure(int indent = 0)
        {
            string indentStr = new string(' ', indent * 2);
            return indentStr + $"Number({Utils.FormatNumeric(_value)})\n";
        }

        public override string Write() => Utils.FormatNumeric(_value);
    }

    public class LiteralString : ExpressionNode
    {
        private string _value;
        public LiteralString(string value) : base("String", 100)
        {
            _value = value;
        }

        public override object Evaluate(Dictionary<string, object> context, List<string>? dumpEval = null)
        {
            if (dumpEval != null)
                dumpEval.Add($"String: {Utils.FormatString(_value)}");
            return _value;
        }

        public override string DumpStructure(int indent = 0)
        {
            string indentStr = new string(' ', indent * 2);
            return indentStr + $"String({Utils.FormatString(_value)})\n";
        }

        public override string Write() => Utils.FormatString(_value);
    }

    public class Variable : ExpressionNode
    {
        private string _name;
        public Variable(string name) : base("Variable", 100)
        {
            _name = name;
        }

        public override object Evaluate(Dictionary<string, object> context, List<string>? dumpEval = null)
        {
            if (!context.ContainsKey(_name))
                throw new Exception($"Variable '{_name}' not found in context.");

            object value = context[_name];
            if (!(value is int || value is double || value is bool || value is string))
                throw new Exception($"Variable '{_name}' must return bool, string, or numeric.");

            if (dumpEval != null)
                dumpEval.Add($"Fetching variable: {_name} -> {Utils.FormatValue(value)}");
            return value;
        }

        public override string DumpStructure(int indent = 0)
        {
            string indentStr = new string(' ', indent * 2);
            return indentStr + $"Variable({_name})\n";
        }

        public override string Write() => _name;
    }

    public class FunctionCall : ExpressionNode
    {
        private string _funcName;
        private List<ExpressionNode> _args;

        public FunctionCall(string funcName, List<ExpressionNode> args) : base("FunctionCall", 100)
        {
            _funcName = funcName;
            _args = args ?? new List<ExpressionNode>();
        }

        public override object Evaluate(Dictionary<string, object> context, List<string>? dumpEval = null)
        {
            if (!context.ContainsKey(_funcName))
                throw new Exception($"Function '{_funcName}' not found in context.");

            object funcObj = context[_funcName];
            if (!(funcObj is Delegate))
                throw new Exception($"Context entry for '{_funcName}' is not a function.");

            Delegate func = (Delegate)funcObj;
            List<object> argValues = new List<object>();
            foreach (var arg in _args)
            {
                argValues.Add(arg.Evaluate(context, dumpEval));
            }

            // A delegate whose sole parameter is object[] is treated as variadic
            // (e.g. Utils.Format) and skips the fixed-count check below; all
            // evaluated args are passed through as a single array argument.
            ParameterInfo[] expectedParams = func.Method.GetParameters();
            bool isVariadic = expectedParams.Length == 1 && expectedParams[0].ParameterType == typeof(object[]);

            object? result;
            if (isVariadic)
            {
                result = func.DynamicInvoke(new object[] { argValues.ToArray() });
            }
            else
            {
                // Strict arity check using reflection on the delegate's method signature.
                if (argValues.Count != expectedParams.Length)
                {
                    string formattedArgs = string.Join(", ", argValues.Select(v => Utils.FormatValue(v)));
                    throw new Exception($"Function '{_funcName}' does not support the provided arguments ({formattedArgs}).");
                }
                result = func.DynamicInvoke(argValues.ToArray());
            }

            if (!(result is int || result is double || result is bool || result is string))
                throw new Exception($"Function '{_funcName}' must return bool, string, or numeric.");

            if (dumpEval != null)
            {
                string formattedArgs = string.Join(", ", argValues.Select(v => Utils.FormatValue(v)));
                dumpEval.Add($"Called function: {_funcName}({formattedArgs}) = {Utils.FormatValue(result)}");
            }

            return result;
        }

        public override string DumpStructure(int indent = 0)
        {
            string indentStr = new string(' ', indent * 2);
            string outStr = indentStr + $"FunctionCall({_funcName})\n";
            foreach (var arg in _args)
            {
                outStr += arg.DumpStructure(indent + 1);
            }
            return outStr;
        }

        public override string Write()
        {
            string argsStr = string.Join(", ", _args.Select(arg => arg.Write()));
            return $"{_funcName}({argsStr})";
        }
    }
}