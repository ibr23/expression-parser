// This file is part of an MIT-licensed project: see LICENSE file or README.md for details.
// Copyright (c) 2025 Ian Thomas

namespace FountainTools.Tests;
using System.IO;
using ExpressionParser;

public class ParserTest
{
    private string loadTestFile(string fileName) {
        // Normalize line endings: the golden files may be checked out with
        // CRLF (e.g. on Windows), but processedLines is always joined with "\n".
        return File.ReadAllText("../../../../../tests/"+fileName).Replace("\r\n", "\n");
    }

    [Fact]
    public void Simple()
    {
        var parser = new Parser();
        var expression = parser.Parse("get_name()=='fred' and counter>0 and 5/5.0!=0");

        var context = new Dictionary<string, object>
        {
            { "get_name", new Func<string>(() => "fred") },
            { "counter", 1 }
        };

        var result = expression.Evaluate(context);

        Assert.Equal(true, result);
    }

    [Fact]
    public void Specificity()
    {
        var parser = new Parser();
        var expression = parser.Parse("get_name()=='fred' and counter>0 and 5/5.0!=0");
        Assert.Equal(2, expression.Specificity);
        expression = parser.Parse("get_name()=='fred' and counter>0 and (5/5.0!=0 or true)");
        Assert.Equal(3, expression.Specificity);
        expression = parser.Parse("true");
        Assert.Equal(0, expression.Specificity);
    }

    [Fact]
    public void DecimalSeparator()
    {
        var parser = new Parser();
        var context = new Dictionary<string, object>
        {
            { "format", new Func<object[], object>(Utils.FormatFunction) }
        };

        Writer.DecimalSeparator = ',';
        try
        {
            // Number literal formatting (Write/DumpStructure) respects the setting.
            var expression = parser.Parse("3.5 + 0.5");
            Assert.Equal("3,5 + 0,5", expression.Write());
            Assert.Equal("Plus\n  Number(3,5)\n  Number(0,5)\n", expression.DumpStructure());

            // format()'s precision output respects it too.
            expression = parser.Parse("format('{0:2}', 3.14159)");
            Assert.Equal("3,14", expression.Evaluate(context));

            // Parsing (MakeNumeric on context string values, same as expression
            // literals) always expects '.' regardless of the configured
            // separator -- the setting only affects formatting/display.
            context["price"] = "3,14";
            expression = parser.Parse("price > 3");
            Assert.Throws<Exception>(() => expression.Evaluate(context));
            context["price"] = "3.14";
            Assert.Equal(true, expression.Evaluate(context));

            // Expression source syntax itself is grammar-fixed at '.', regardless
            // of the configured separator -- it still parses and evaluates
            // correctly...
            expression = parser.Parse("3.14 > 3");
            Assert.Equal(true, expression.Evaluate(context));
            // ...and ',' can't be used as a literal decimal point, since it's
            // already the function-argument / format-width separator token.
            Assert.Throws<SyntaxErrorException>(() => parser.Parse("3,14"));
        }
        finally
        {
            Writer.DecimalSeparator = '.';
        }
    }

    [Fact]
    public void Format()
    {
        var parser = new Parser();
        var context = new Dictionary<string, object>
        {
            { "format", new Func<object[], object>(Utils.FormatFunction) },
            { "name", "fred" },
            { "age", 7 },
            { "pi", 3.14159 }
        };

        // Basic positional substitution.
        var expression = parser.Parse("format('{0} is {1} years old', name, age)");
        Assert.Equal("fred is 7 years old", expression.Evaluate(context));

        // Repeated / reordered indices.
        expression = parser.Parse("format('{1} {0} {1}', 'a', 'b')");
        Assert.Equal("b a b", expression.Evaluate(context));

        // Precision, applies only to numeric args.
        expression = parser.Parse("format('pi={0:2}', pi)");
        Assert.Equal("pi=3.14", expression.Evaluate(context));
        expression = parser.Parse("format('{0:2}', name)");
        Assert.Equal("fred", expression.Evaluate(context));

        // Width/alignment: positive right-aligns, negative left-aligns.
        expression = parser.Parse("format('[{0,10}]', name)");
        Assert.Equal("[      fred]", expression.Evaluate(context));
        expression = parser.Parse("format('[{0,-10}]', name)");
        Assert.Equal("[fred      ]", expression.Evaluate(context));

        // Width does nothing if the value is already at least that long.
        expression = parser.Parse("format('[{0,2}]', name)");
        Assert.Equal("[fred]", expression.Evaluate(context));

        // Width + precision combined.
        expression = parser.Parse("format('[{0,10:2}]', pi)");
        Assert.Equal("[      3.14]", expression.Evaluate(context));

        // Escaped literal braces.
        expression = parser.Parse("format('{{literal}} {0}', name)");
        Assert.Equal("{literal} fred", expression.Evaluate(context));

        // Out-of-range index throws. Delegate.DynamicInvoke wraps exceptions
        // thrown inside a context-registered function, hence the target type.
        expression = parser.Parse("format('{1}', name)");
        Assert.Throws<System.Reflection.TargetInvocationException>(() => expression.Evaluate(context));
    }

    [Fact]
    public void Concat()
    {
        var parser = new Parser();
        var context = new Dictionary<string, object>();

        // Basic string concatenation.
        var expression = parser.Parse("'foo' .. 'bar'");
        Assert.Equal("foobar", expression.Evaluate(context));

        // Non-string operands are coerced to string, not added numerically.
        expression = parser.Parse("'count: ' .. 5");
        Assert.Equal("count: 5", expression.Evaluate(context));

        expression = parser.Parse("5 .. 6");
        Assert.Equal("56", expression.Evaluate(context));

        // Left-associative chaining: (a .. b) .. c
        expression = parser.Parse("'a' .. 'b' .. 'c'");
        Assert.Equal("abc", expression.Evaluate(context));

        // Binds tighter than comparisons but looser than +/-.
        context["name"] = "fred";
        expression = parser.Parse("'hi ' .. name == 'hi fred'");
        Assert.Equal(true, expression.Evaluate(context));

        expression = parser.Parse("'total: ' .. 1 + 2");
        Assert.Equal("total: 3", expression.Evaluate(context));
    }

    [Fact]
    public void NumericEquals()
    {
        var parser = new Parser();

        // An int-typed context variable must compare equal to a numeric literal
        // (numeric literals always parse as double, so this exercises int vs.
        // double equality, not just double vs. double).
        var context = new Dictionary<string, object>
        {
            { "counter", 2 }
        };

        var expression = parser.Parse("counter == 2");
        Assert.Equal(true, expression.Evaluate(context));

        expression = parser.Parse("counter != 3");
        Assert.Equal(true, expression.Evaluate(context));

        expression = parser.Parse("counter == 3");
        Assert.Equal(false, expression.Evaluate(context));

        // double-typed context variable, for symmetry.
        context["ratio"] = 0.5;
        expression = parser.Parse("ratio == 0.5");
        Assert.Equal(true, expression.Evaluate(context));
    }

    [Fact]
    public void MatchOutput()
    {
        string source = loadTestFile("Parse.txt");

        string[] lines = source.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);

        var context = new Dictionary<string, object>
        {
            { "C", 15 },
            { "D", false },
            { "get_name", new Func<string>(() => "fred") },
            { "end_func", new Func<bool>(() => true) },
            { "whisky", new Func<string, double, string>((id, n) => ((int)n).ToString() + "whisky_" + id) },
            { "counter", 1 }
        };

        var parser = new Parser();

        var processedLines = new List<string>();
        foreach (var line in lines)
        {
            if (line.StartsWith("//"))
            {
                processedLines.Add(line);
                continue;
            }

            processedLines.Add($"\"{line}\"");
            try
            {
                var node = parser.Parse(line);
                processedLines.Add(node.DumpStructure());

                var dumpEval = new List<string>();
                node.Evaluate(context, dumpEval);
                processedLines.Add(string.Join("\n", dumpEval));
            }
            catch (Exception e)
            {
                processedLines.Add(e.Message);
            }
            processedLines.Add("");
        }

        string output = string.Join("\n", processedLines);
                
        //Console.WriteLine(output);

        string match = loadTestFile("Parse-Output.txt");
        Assert.Equal(match, output);
    }
}
