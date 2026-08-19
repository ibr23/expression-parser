/*
 * This file is part of an MIT-licensed project: see LICENSE file or README.md for details.
 * Copyright (c) 2025 Ian Thomas
 */

namespace ExpressionParser
{
    public static class Writer
    {
        public enum STRING_FORMAT {
            SINGLEQUOTE = 0,
            ESCAPED_SINGLEQUOTE = 1,
            DOUBLEQUOTE = 2,
            ESCAPED_DOUBLEQUOTE = 3
        }

        private static STRING_FORMAT _stringFormat = STRING_FORMAT.SINGLEQUOTE;
        public static STRING_FORMAT StringFormat
        {
            get { return _stringFormat; }
            set { _stringFormat = value; }
        }

        // Decimal separator used when formatting numbers to strings (FormatNumeric,
        // Format()'s precision output). Does NOT affect parsing: numeric literals
        // in expression source, and numeric strings coming from context values
        // (MakeNumeric), are always parsed with '.' as the decimal point,
        // regardless of this setting -- '.' is part of the expression grammar
        // (as is ',', used for function-argument and format-spec-width
        // separators), so making parsing culture-variable would be ambiguous.
        // Default '.'.
        private static char _decimalSeparator = '.';
        public static char DecimalSeparator
        {
            get { return _decimalSeparator; }
            set { _decimalSeparator = value; }
        }
    }
}