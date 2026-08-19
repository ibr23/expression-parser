/*
 * This file is part of an MIT-licensed project: see LICENSE file or README.md for details.
 * Copyright (c) 2025 Ian Thomas
 */

 #ifndef WRITER_H
 #define WRITER_H
 
 namespace ExpressionParser {
 
 enum class STRING_FORMAT {
     SINGLEQUOTE = 0,
     ESCAPED_SINGLEQUOTE = 1,
     DOUBLEQUOTE = 2,
     ESCAPED_DOUBLEQUOTE = 3
 };
 
 class Writer {
 public:
     Writer() = delete; // Prevent instantiation

     static STRING_FORMAT getStringFormat();
     static void setStringFormat(STRING_FORMAT format);

     // Decimal separator used when formatting numbers to strings (FormatNumeric,
     // format()'s precision output). Does NOT affect parsing: numeric literals
     // in expression source, and numeric strings coming from context values
     // (MakeNumeric), are always parsed with '.' as the decimal point,
     // regardless of this setting -- '.' is part of the expression grammar
     // (as is ',', used for function-argument and format-spec-width
     // separators), so making parsing culture-variable would be ambiguous.
     // Default '.'.
     static char getDecimalSeparator();
     static void setDecimalSeparator(char separator);

 private:
     static STRING_FORMAT _stringFormat;
     static char _decimalSeparator;
 };
 
 } // namespace ExpressionParser
 
 #endif // WRITER_H