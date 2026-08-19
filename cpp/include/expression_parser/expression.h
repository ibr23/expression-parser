/*
 * This file is part of an MIT-licensed project: see LICENSE file or README.md for details.
 * Copyright (c) 2025 Ian Thomas
 */

#ifndef EXPRESSION_H
#define EXPRESSION_H

#include <string>
#include <vector>
#include <unordered_map>
#include <any>
#include <functional>
#include <memory>
#include <stdexcept>
#include <regex>
#include "context.h"

namespace ExpressionParser {

// ---------------------
// Utility functions
// ---------------------
namespace Utils {

    bool MakeBool(const std::any &val);
    double MakeNumeric(const std::any &val);
    std::string MakeString(const std::any &val);
    std::any MakeTypeMatch(const std::any &leftVal, const std::any &rightVal);
    bool AnyEquals(const std::any &a, const std::any &b);

    std::string FormatBoolean(bool val);
    std::string FormatNumeric(double num);
    std::string FormatString(const std::string &val);
    std::string FormatValue(const std::any &val);

    // fmt-style string formatting. Placeholders look like {index}, {index,width},
    // {index:precision}, or {index,width:precision}:
    //   index     - 0-based, selects args[index].
    //   width     - signed; positive right-aligns (pads left), negative
    //               left-aligns (pads right), to abs(width) characters.
    //   precision - decimal places; only applies when the arg is int/double,
    //               ignored otherwise.
    // Literal braces are written as {{ and }}. Values are stringified via
    // MakeString (unquoted), not FormatValue.
    std::string Format(const std::string &fmtStr, const std::vector<std::any> &args);
}

// Convenience FunctionWrapper for registering Utils::Format as a context
// function, e.g. context["format"] = make_format_function_wrapper();
// Usage: format("{0} is {1:2} years old", name, age)
FunctionWrapper make_format_function_wrapper();

// ---------------------
// Base Node
// ---------------------
class ExpressionNode {
public:
    std::string Name;
    int Precedence;
    int GetSpecificity() const {return _specificity;}

    ExpressionNode(const std::string &name, int precedence)
        : Name(name), Precedence(precedence) {}

    virtual ~ExpressionNode() = default;
    virtual std::any Evaluate(const Context &context, std::vector<std::string>* dumpEval = nullptr) const = 0;
    virtual std::string DumpStructure(int indent = 0) const = 0;
    virtual std::string Write() const = 0;

protected:
    int _specificity = 0;
};

// ---------------------
// Binary Operators
// ---------------------
class BinaryOp : public ExpressionNode {
protected:
    std::shared_ptr<ExpressionNode> Left;
    std::shared_ptr<ExpressionNode> Right;
    std::string Op;
public:
    BinaryOp(const std::string &name, std::shared_ptr<ExpressionNode> left, const std::string &op,
             std::shared_ptr<ExpressionNode> right, int precedence);

    virtual std::any Evaluate(const Context &context, std::vector<std::string>* dumpEval = nullptr) const override;
    virtual std::string DumpStructure(int indent = 0) const override;
    virtual std::string Write() const override;
protected:
    virtual std::pair<bool, std::any> ShortCircuit(const std::any &leftVal) const;
    virtual std::any DoEval(const std::any &leftVal, const std::any &rightVal) const = 0;
};

class OpOr : public BinaryOp {
public:
    OpOr(std::shared_ptr<ExpressionNode> left, std::shared_ptr<ExpressionNode> right);
protected:
    virtual std::pair<bool, std::any> ShortCircuit(const std::any& leftVal) const override;
    virtual std::any DoEval(const std::any &leftVal, const std::any &rightVal) const override;
};

class OpAnd : public BinaryOp {
public:
    OpAnd(std::shared_ptr<ExpressionNode> left, std::shared_ptr<ExpressionNode> right);
protected:
    virtual std::pair<bool, std::any> ShortCircuit(const std::any& leftVal) const override;
    virtual std::any DoEval(const std::any &leftVal, const std::any &rightVal) const override;
};

class OpEquals : public BinaryOp {
public:
    OpEquals(std::shared_ptr<ExpressionNode> left, std::shared_ptr<ExpressionNode> right);
protected:
    virtual std::any DoEval(const std::any &leftVal, const std::any &rightVal) const override;
};

class OpNotEquals : public BinaryOp {
public:
    OpNotEquals(std::shared_ptr<ExpressionNode> left, std::shared_ptr<ExpressionNode> right);
protected:
    virtual std::any DoEval(const std::any &leftVal, const std::any &rightVal) const override;
};

class OpPlus : public BinaryOp {
public:
    OpPlus(std::shared_ptr<ExpressionNode> left, std::shared_ptr<ExpressionNode> right);
protected:
    virtual std::any DoEval(const std::any &leftVal, const std::any &rightVal) const override;
};

class OpMinus : public BinaryOp {
public:
    OpMinus(std::shared_ptr<ExpressionNode> left, std::shared_ptr<ExpressionNode> right);
protected:
    virtual std::any DoEval(const std::any &leftVal, const std::any &rightVal) const override;
};

class OpDivide : public BinaryOp {
public:
    OpDivide(std::shared_ptr<ExpressionNode> left, std::shared_ptr<ExpressionNode> right);
protected:
    virtual std::any DoEval(const std::any &leftVal, const std::any &rightVal) const override;
};

class OpMultiply : public BinaryOp {
public:
    OpMultiply(std::shared_ptr<ExpressionNode> left, std::shared_ptr<ExpressionNode> right);
protected:
    virtual std::pair<bool, std::any> ShortCircuit(const std::any& leftVal) const override;
    virtual std::any DoEval(const std::any &leftVal, const std::any &rightVal) const override;
};

class OpGreaterThan : public BinaryOp {
public:
    OpGreaterThan(std::shared_ptr<ExpressionNode> left, std::shared_ptr<ExpressionNode> right);
protected:
    virtual std::any DoEval(const std::any &leftVal, const std::any &rightVal) const override;
};

class OpLessThan : public BinaryOp {
public:
    OpLessThan(std::shared_ptr<ExpressionNode> left, std::shared_ptr<ExpressionNode> right);
protected:
    virtual std::any DoEval(const std::any &leftVal, const std::any &rightVal) const override;
};

class OpGreaterThanEquals : public BinaryOp {
public:
    OpGreaterThanEquals(std::shared_ptr<ExpressionNode> left, std::shared_ptr<ExpressionNode> right);
protected:
    virtual std::any DoEval(const std::any &leftVal, const std::any &rightVal) const override;
};

class OpLessThanEquals : public BinaryOp {
public:
    OpLessThanEquals(std::shared_ptr<ExpressionNode> left, std::shared_ptr<ExpressionNode> right);
protected:
    virtual std::any DoEval(const std::any &leftVal, const std::any &rightVal) const override;
};

// String concatenation: left .. right. Both operands are coerced to string
// regardless of type (numbers/booleans are formatted, not added numerically).
class OpConcat : public BinaryOp {
public:
    OpConcat(std::shared_ptr<ExpressionNode> left, std::shared_ptr<ExpressionNode> right);
protected:
    virtual std::any DoEval(const std::any &leftVal, const std::any &rightVal) const override;
};

// ---------------------
// Ternary Conditional Operator
// ---------------------
// condition ? trueExpr : falseExpr
class OpTernary : public ExpressionNode {
protected:
    std::shared_ptr<ExpressionNode> Condition;
    std::shared_ptr<ExpressionNode> TrueExpr;
    std::shared_ptr<ExpressionNode> FalseExpr;
public:
    OpTernary(std::shared_ptr<ExpressionNode> condition, std::shared_ptr<ExpressionNode> trueExpr,
              std::shared_ptr<ExpressionNode> falseExpr);

    virtual std::any Evaluate(const Context &context, std::vector<std::string>* dumpEval = nullptr) const override;
    virtual std::string DumpStructure(int indent = 0) const override;
    virtual std::string Write() const override;
};

// ---------------------
// Unary Operators
// ---------------------
class UnaryOp : public ExpressionNode {
protected:
    std::shared_ptr<ExpressionNode> Operand;
    std::string Op;
public:
    UnaryOp(const std::string &name, const std::string &op, std::shared_ptr<ExpressionNode> operand, int precedence);
    virtual std::any Evaluate(const Context &context, std::vector<std::string>* dumpEval = nullptr) const override;
    virtual std::string DumpStructure(int indent = 0) const override;
    virtual std::string Write() const override;
protected:
    virtual std::any DoEval(const std::any &val) const = 0;
};

class OpNegative : public UnaryOp {
public:
    OpNegative(std::shared_ptr<ExpressionNode> operand);
protected:
    virtual std::any DoEval(const std::any &val) const override;
};

class OpNot : public UnaryOp {
public:
    OpNot(std::shared_ptr<ExpressionNode> operand);
protected:
    virtual std::any DoEval(const std::any &val) const override;
};

// ---------------------
// Literal Nodes
// ---------------------
class LiteralBoolean : public ExpressionNode {
    bool value;
public:
    LiteralBoolean(bool val);
    virtual std::any Evaluate(const Context &context, std::vector<std::string>* dumpEval = nullptr) const override;
    virtual std::string DumpStructure(int indent = 0) const override;
    virtual std::string Write() const override;
};

class LiteralNumber : public ExpressionNode {
    double value;
public:
    LiteralNumber(const std::string &val);
    virtual std::any Evaluate(const Context &context, std::vector<std::string>* dumpEval = nullptr) const override;
    virtual std::string DumpStructure(int indent = 0) const override;
    virtual std::string Write() const override;
};

class LiteralString : public ExpressionNode {
    std::string value;
public:
    LiteralString(const std::string &val);
    virtual std::any Evaluate(const Context &context, std::vector<std::string>* dumpEval = nullptr) const override;
    virtual std::string DumpStructure(int indent = 0) const override;
    virtual std::string Write() const override;
};

// ---------------------
// Variable
// ---------------------

class Variable : public ExpressionNode {
    std::string name;
public:
    Variable(const std::string &name);
    virtual std::any Evaluate(const Context &context, std::vector<std::string>* dumpEval = nullptr) const override;
    virtual std::string DumpStructure(int indent = 0) const override;
    virtual std::string Write() const override;
};

// ---------------------
// Function Call
// ---------------------

class FunctionCall : public ExpressionNode {
    std::string funcName;
    std::vector<std::shared_ptr<ExpressionNode>> args;
public:
    FunctionCall(const std::string &funcName, const std::vector<std::shared_ptr<ExpressionNode>> &args);
    virtual std::any Evaluate(const Context &context, std::vector<std::string>* dumpEval = nullptr) const override;
    virtual std::string DumpStructure(int indent = 0) const override;
    virtual std::string Write() const override;
};

} // namespace ExpressionParser

#endif // EXPRESSION_H