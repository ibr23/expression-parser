// This file is part of an MIT-licensed project: see LICENSE file or README.md for details.
// Copyright (c) 2025 Ian Thomas

namespace FountainTools.Tests;
using System.IO;
using ExpressionParser;

public class WriterTest
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
        
        var result = expression.Write();
        Assert.Equal("get_name() == 'fred' and counter > 0 and 5 / 5 != 0", result);
        Writer.StringFormat = Writer.STRING_FORMAT.DOUBLEQUOTE;
        result = expression.Write();
        Assert.Equal("get_name() == \"fred\" and counter > 0 and 5 / 5 != 0", result);
        Writer.StringFormat = Writer.STRING_FORMAT.ESCAPED_DOUBLEQUOTE;
        result = expression.Write();
        Assert.Equal("get_name() == \\\"fred\\\" and counter > 0 and 5 / 5 != 0", result);
        Writer.StringFormat = Writer.STRING_FORMAT.ESCAPED_SINGLEQUOTE;
        result = expression.Write();
        Assert.Equal("get_name() == \\'fred\\' and counter > 0 and 5 / 5 != 0", result);
        Writer.StringFormat = Writer.STRING_FORMAT.SINGLEQUOTE;
    }

    [Fact]
    public void ConcatWriter()
    {
        var parser = new Parser();

        var expression = parser.Parse("'foo'..'bar'");
        Assert.Equal("'foo' .. 'bar'", expression.Write());

        // Math binds tighter than concat, so no parens needed round-tripping.
        expression = parser.Parse("'total: ' .. 1 + 2");
        Assert.Equal("'total: ' .. 1 + 2", expression.Write());

        // A lower-precedence expression used as an operand (via explicit parens)
        // must keep its parens when written back.
        expression = parser.Parse("(true or false) .. 'x'");
        Assert.Equal("(true or false) .. 'x'", expression.Write());
    }

    [Fact]
    public void MatchOutput()
    {
        string source = loadTestFile("Writer.txt");

        string[] lines = source.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);

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
                var expression = parser.Parse(line);
                processedLines.Add(expression.Write());
            }
            catch (Exception e)
            {
                processedLines.Add(e.Message);
            }
            processedLines.Add("");
        }

        string output = string.Join("\n", processedLines);
                
        //Console.WriteLine(output);

        string match = loadTestFile("Writer-Output.txt");
        Assert.Equal(match, output);
    }
}
