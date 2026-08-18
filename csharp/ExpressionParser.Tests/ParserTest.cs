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
