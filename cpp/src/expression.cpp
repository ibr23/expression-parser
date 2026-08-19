#include "expression_parser/expression.h"
#include "expression_parser/writer.h"
#include <sstream>
#include <cmath>
#include <stdexcept>
#include <algorithm>
#include <iomanip>
#include <regex>
#include <charconv>
#include <locale>

namespace ExpressionParser {

// ---------------------
// Utils implementations
// ---------------------
namespace Utils {

bool MakeBool(const std::any &val) {
    if (val.type() == typeid(bool))
        return std::any_cast<bool>(val);
    if (val.type() == typeid(int))
        return std::any_cast<int>(val) != 0;
    if (val.type() == typeid(double))
        return std::any_cast<double>(val) != 0;
    if (val.type() == typeid(std::string)) {
        std::string s = std::any_cast<std::string>(val);
        std::transform(s.begin(), s.end(), s.begin(), ::tolower);
        return (s == "true" || s == "1");
    }
    throw std::runtime_error("Type mismatch: Expecting bool");
}

double MakeNumeric(const std::any &val) {
    if (val.type() == typeid(bool))
        return std::any_cast<bool>(val) ? 1.0 : 0.0;
    if (val.type() == typeid(int))
        return static_cast<double>(std::any_cast<int>(val));
    if (val.type() == typeid(double))
        return std::any_cast<double>(val);
    if (val.type() == typeid(std::string)) {
        // Parsing always uses '.' as the decimal point, same as expression
        // literals -- Writer::DecimalSeparator only affects formatting/display,
        // not parsing. std::from_chars is locale-independent by spec, so this
        // can't drift with the host process's global C locale either.
        const std::string &s = std::any_cast<std::string>(val);
        double result = 0.0;
        auto [ptr, ec] = std::from_chars(s.data(), s.data() + s.size(), result);
        if (ec != std::errc() || ptr != s.data() + s.size())
            throw std::runtime_error("Type mismatch: Expecting number but got '" + s + "'");
        return result;
    }
    throw std::runtime_error("Type mismatch: Expecting number");
}

std::string MakeString(const std::any &val) {
    if (val.type() == typeid(std::string))
        return std::any_cast<std::string>(val);
    if (val.type() == typeid(bool))
        return std::any_cast<bool>(val) ? "true" : "false";
    if (val.type() == typeid(int))
        return std::to_string(std::any_cast<int>(val));
    if (val.type() == typeid(double))
        return FormatNumeric(std::any_cast<double>(val));
    throw std::runtime_error("Type mismatch: Expecting string");
}

std::any MakeTypeMatch(const std::any &leftVal, const std::any &rightVal) {
    if (leftVal.type() == typeid(bool))
        return MakeBool(rightVal);
    if (leftVal.type() == typeid(int) || leftVal.type() == typeid(double))
        return MakeNumeric(rightVal);
    if (leftVal.type() == typeid(std::string))
        return MakeString(rightVal);
    throw std::runtime_error("Type mismatch: unrecognised type");
}

bool AnyEquals(const std::any &a, const std::any &b) {
    // Numeric types compare by value regardless of whether they're stored
    // as int or double (MakeTypeMatch always coerces the right-hand side
    // of a comparison to double, so a stricter type check here would make
    // e.g. an int context variable never equal a numeric literal).
    bool aNumeric = (a.type() == typeid(int) || a.type() == typeid(double));
    bool bNumeric = (b.type() == typeid(int) || b.type() == typeid(double));
    if (aNumeric && bNumeric)
        return MakeNumeric(a) == MakeNumeric(b);

    // Otherwise, if the types don't match, we consider them unequal.
    if (a.type() != b.type())
        return false;

    if (a.type() == typeid(bool))
        return std::any_cast<bool>(a) == std::any_cast<bool>(b);
    else if (a.type() == typeid(std::string))
        return std::any_cast<std::string>(a) == std::any_cast<std::string>(b);
    else
        throw std::runtime_error("Unsupported type for equality comparison");
}

std::string FormatBoolean(bool val) {
    return val ? "true" : "false";
}

std::string FormatNumeric(double num) {
    std::string s;
    if (std::fmod(num, 1.0) == 0.0) {
        s = std::to_string(static_cast<int>(num));
    } else {
        // Imbue the classic locale so this doesn't drift if the host process
        // has changed the global C++ locale (e.g. via std::locale::global);
        // the configured decimal separator is applied afterwards instead.
        std::ostringstream oss;
        oss.imbue(std::locale::classic());
        oss << num;
        s = oss.str();
    }
    char sep = Writer::getDecimalSeparator();
    if (sep != '.') {
        for (char &c : s) if (c == '.') c = sep;
    }
    return s;
}

std::string FormatString(const std::string &val) {
    switch (Writer::getStringFormat())
    {
        case STRING_FORMAT::SINGLEQUOTE:
            return "'"+val+"'";
        case STRING_FORMAT::ESCAPED_SINGLEQUOTE:
            return "\\'"+val+"\\'";
        case STRING_FORMAT::ESCAPED_DOUBLEQUOTE:
            return "\\\""+val+"\\\"";
        case STRING_FORMAT::DOUBLEQUOTE:
        default:
            return "\""+val+"\"";
    }
}

std::string FormatValue(const std::any &val) {
    if (val.type() == typeid(bool))
        return FormatBoolean(std::any_cast<bool>(val));
    if (val.type() == typeid(int))
        return std::to_string(std::any_cast<int>(val));
    if (val.type() == typeid(double))
        return FormatNumeric(std::any_cast<double>(val));
    if (val.type() == typeid(std::string))
        return FormatString(std::any_cast<std::string>(val));
    return "";
}

namespace {
    // Parses "index[,width][:precision]" from inside a {...} placeholder.
    struct FormatSpec {
        int index = 0;
        bool hasWidth = false;
        int width = 0;
        bool hasPrecision = false;
        int precision = 0;
    };

    FormatSpec ParseFormatSpec(const std::string &spec) {
        static const std::regex specRegex(R"(^(\d+)(?:,(-?\d+))?(?::(\d+))?$)");
        std::smatch m;
        if (!std::regex_match(spec, m, specRegex))
            throw std::runtime_error("Invalid format placeholder '{" + spec + "}'.");
        FormatSpec result;
        result.index = std::stoi(m[1].str());
        if (m[2].matched) {
            result.hasWidth = true;
            result.width = std::stoi(m[2].str());
        }
        if (m[3].matched) {
            result.hasPrecision = true;
            result.precision = std::stoi(m[3].str());
        }
        return result;
    }

    std::string PadToWidth(const std::string &str, int width) {
        int absWidth = std::abs(width);
        if (static_cast<int>(str.size()) >= absWidth)
            return str;
        std::string pad(absWidth - str.size(), ' ');
        return (width < 0) ? (str + pad) : (pad + str);
    }
} // namespace

std::string Format(const std::string &fmtStr, const std::vector<std::any> &args) {
    std::string result;
    size_t i = 0;
    while (i < fmtStr.size()) {
        char c = fmtStr[i];
        if (c == '{') {
            if (i + 1 < fmtStr.size() && fmtStr[i + 1] == '{') {
                result += '{';
                i += 2;
                continue;
            }
            size_t end = fmtStr.find('}', i + 1);
            if (end == std::string::npos)
                throw std::runtime_error("Unmatched '{' in format string.");
            FormatSpec parsed = ParseFormatSpec(fmtStr.substr(i + 1, end - i - 1));
            if (parsed.index < 0 || static_cast<size_t>(parsed.index) >= args.size())
                throw std::runtime_error("Format index " + std::to_string(parsed.index) + " out of range.");
            const std::any &val = args[parsed.index];
            std::string valStr;
            if (parsed.hasPrecision && (val.type() == typeid(int) || val.type() == typeid(double))) {
                std::ostringstream oss;
                oss.imbue(std::locale::classic());
                oss << std::fixed << std::setprecision(parsed.precision) << MakeNumeric(val);
                valStr = oss.str();
                char sep = Writer::getDecimalSeparator();
                if (sep != '.') {
                    for (char &ch : valStr) if (ch == '.') ch = sep;
                }
            } else {
                valStr = MakeString(val);
            }
            if (parsed.hasWidth)
                valStr = PadToWidth(valStr, parsed.width);
            result += valStr;
            i = end + 1;
            continue;
        }
        if (c == '}') {
            if (i + 1 < fmtStr.size() && fmtStr[i + 1] == '}') {
                result += '}';
                i += 2;
                continue;
            }
            throw std::runtime_error("Unmatched '}' in format string.");
        }
        result += c;
        ++i;
    }
    return result;
}

} // namespace Utils

FunctionWrapper make_format_function_wrapper() {
    return make_variadic_function_wrapper([](const std::vector<std::any> &args) -> std::any {
        if (args.empty())
            throw std::runtime_error("format requires at least a format string argument.");
        std::string fmtStr = Utils::MakeString(args[0]);
        std::vector<std::any> rest(args.begin() + 1, args.end());
        return Utils::Format(fmtStr, rest);
    });
}

// ---------------------
// BinaryOp implementations
// ---------------------
BinaryOp::BinaryOp(const std::string &name, std::shared_ptr<ExpressionNode> left, const std::string &op,
                   std::shared_ptr<ExpressionNode> right, int precedence)
    : ExpressionNode(name, precedence), Left(left), Right(right), Op(op) {
        this->_specificity = left->GetSpecificity() + right->GetSpecificity();
    }

std::any BinaryOp::Evaluate(const Context &context, std::vector<std::string>* dumpEval) const {
    std::any leftVal = Left->Evaluate(context, dumpEval);

    auto [shortCircuit, shortCircuitResult] = ShortCircuit(leftVal);
    if (shortCircuit)
    {
        if (dumpEval != nullptr)
        {
            dumpEval->push_back("Evaluated: " + Utils::FormatValue(leftVal) + " " + Op + " (ignore) = " + Utils::FormatValue(shortCircuitResult));
        }
        return shortCircuitResult;
    }

    std::any rightVal = Right->Evaluate(context, dumpEval);
    std::any result = DoEval(leftVal, rightVal);
    if (dumpEval) {
        dumpEval->push_back("Evaluated: " + Utils::FormatValue(leftVal) + " " + Op + " " +
                              Utils::FormatValue(rightVal) + " = " + Utils::FormatValue(result));
    }
    return result;
}

std::pair<bool, std::any> BinaryOp::ShortCircuit(const std::any& leftVal) const {
    return { false, std::any() };
}

std::string BinaryOp::DumpStructure(int indent) const {
    std::string indentStr(indent * 2, ' ');
    return indentStr + Name + "\n" +
           Left->DumpStructure(indent + 1) +
           Right->DumpStructure(indent + 1);
}

std::string BinaryOp::Write() const {
    std::string leftStr = Left->Write();
    std::string rightStr = Right->Write();
    if (Left->Precedence < this->Precedence)
        leftStr = "(" + leftStr + ")";
    if (Right->Precedence < this->Precedence)
        rightStr = "(" + rightStr + ")";
    return leftStr + " " + Op + " " + rightStr;
}

// Concrete BinaryOp classes
OpOr::OpOr(std::shared_ptr<ExpressionNode> left, std::shared_ptr<ExpressionNode> right)
    : BinaryOp("Or", left, "or", right, 40) {
        this->_specificity+=1;
    }

std::pair<bool, std::any> OpOr::ShortCircuit(const std::any& leftVal) const {

    bool result = Utils::MakeBool(leftVal);
    if (result)
        return {true, true};
    return { false, std::any() };
}

std::any OpOr::DoEval(const std::any &leftVal, const std::any &rightVal) const {
    return Utils::MakeBool(leftVal) || Utils::MakeBool(rightVal);
}

OpAnd::OpAnd(std::shared_ptr<ExpressionNode> left, std::shared_ptr<ExpressionNode> right)
    : BinaryOp("And", left, "and", right, 50) {
        this->_specificity+=1;
    }

std::pair<bool, std::any> OpAnd::ShortCircuit(const std::any& leftVal) const {

    bool result = Utils::MakeBool(leftVal);
    if (!result)
        return {true, false};
    return { false, std::any() };
}

std::any OpAnd::DoEval(const std::any &leftVal, const std::any &rightVal) const {
    return Utils::MakeBool(leftVal) && Utils::MakeBool(rightVal);
}

OpEquals::OpEquals(std::shared_ptr<ExpressionNode> left, std::shared_ptr<ExpressionNode> right)
    : BinaryOp("Equals", left, "==", right, 60) {}

std::any OpEquals::DoEval(const std::any &leftVal, const std::any &rightVal) const {
    std::any rVal = Utils::MakeTypeMatch(leftVal, rightVal);
    return Utils::AnyEquals(leftVal, rVal);
}

OpNotEquals::OpNotEquals(std::shared_ptr<ExpressionNode> left, std::shared_ptr<ExpressionNode> right)
    : BinaryOp("NotEquals", left, "!=", right, 60) {}

std::any OpNotEquals::DoEval(const std::any &leftVal, const std::any &rightVal) const {
    std::any rVal = Utils::MakeTypeMatch(leftVal, rightVal);
    return !Utils::AnyEquals(leftVal, rVal);
}

OpPlus::OpPlus(std::shared_ptr<ExpressionNode> left, std::shared_ptr<ExpressionNode> right)
    : BinaryOp("Plus", left, "+", right, 70) {}

std::any OpPlus::DoEval(const std::any &leftVal, const std::any &rightVal) const {
    return Utils::MakeNumeric(leftVal) + Utils::MakeNumeric(rightVal);
}

OpMinus::OpMinus(std::shared_ptr<ExpressionNode> left, std::shared_ptr<ExpressionNode> right)
    : BinaryOp("Minus", left, "-", right, 70) {}

std::any OpMinus::DoEval(const std::any &leftVal, const std::any &rightVal) const {
    return Utils::MakeNumeric(leftVal) - Utils::MakeNumeric(rightVal);
}

OpDivide::OpDivide(std::shared_ptr<ExpressionNode> left, std::shared_ptr<ExpressionNode> right)
    : BinaryOp("Divide", left, "/", right, 85) {}

std::any OpDivide::DoEval(const std::any &leftVal, const std::any &rightVal) const {
    double numRight = Utils::MakeNumeric(rightVal);
    if (numRight == 0)
        throw std::runtime_error("Division by zero.");
    return Utils::MakeNumeric(leftVal) / numRight;
}

OpMultiply::OpMultiply(std::shared_ptr<ExpressionNode> left, std::shared_ptr<ExpressionNode> right)
    : BinaryOp("Multiply", left, "*", right, 80) {}

std::pair<bool, std::any> OpMultiply::ShortCircuit(const std::any& leftVal) const {

    double result = Utils::MakeNumeric(leftVal);
    if (result==0.0)
        return {true, 0.0};
    return { false, std::any() };
}

std::any OpMultiply::DoEval(const std::any &leftVal, const std::any &rightVal) const {
    return Utils::MakeNumeric(leftVal) * Utils::MakeNumeric(rightVal);
}

OpGreaterThan::OpGreaterThan(std::shared_ptr<ExpressionNode> left, std::shared_ptr<ExpressionNode> right)
    : BinaryOp("GreaterThan", left, ">", right, 60) {}

std::any OpGreaterThan::DoEval(const std::any &leftVal, const std::any &rightVal) const {
    return Utils::MakeNumeric(leftVal) > Utils::MakeNumeric(rightVal);
}

OpLessThan::OpLessThan(std::shared_ptr<ExpressionNode> left, std::shared_ptr<ExpressionNode> right)
    : BinaryOp("LessThan", left, "<", right, 60) {}

std::any OpLessThan::DoEval(const std::any &leftVal, const std::any &rightVal) const {
    return Utils::MakeNumeric(leftVal) < Utils::MakeNumeric(rightVal);
}

OpGreaterThanEquals::OpGreaterThanEquals(std::shared_ptr<ExpressionNode> left, std::shared_ptr<ExpressionNode> right)
    : BinaryOp("GreaterThanEquals", left, ">=", right, 60) {}

std::any OpGreaterThanEquals::DoEval(const std::any &leftVal, const std::any &rightVal) const {
    return Utils::MakeNumeric(leftVal) >= Utils::MakeNumeric(rightVal);
}

OpLessThanEquals::OpLessThanEquals(std::shared_ptr<ExpressionNode> left, std::shared_ptr<ExpressionNode> right)
    : BinaryOp("LessThanEquals", left, "<=", right, 60) {}

std::any OpLessThanEquals::DoEval(const std::any &leftVal, const std::any &rightVal) const {
    return Utils::MakeNumeric(leftVal) <= Utils::MakeNumeric(rightVal);
}

OpConcat::OpConcat(std::shared_ptr<ExpressionNode> left, std::shared_ptr<ExpressionNode> right)
    : BinaryOp("Concat", left, "..", right, 65) {}

std::any OpConcat::DoEval(const std::any &leftVal, const std::any &rightVal) const {
    return Utils::MakeString(leftVal) + Utils::MakeString(rightVal);
}

// ---------------------
// OpTernary implementation
// ---------------------
OpTernary::OpTernary(std::shared_ptr<ExpressionNode> condition, std::shared_ptr<ExpressionNode> trueExpr,
                      std::shared_ptr<ExpressionNode> falseExpr)
    : ExpressionNode("Ternary", 30), Condition(condition), TrueExpr(trueExpr), FalseExpr(falseExpr) {
        this->_specificity = condition->GetSpecificity() + trueExpr->GetSpecificity() + falseExpr->GetSpecificity();
    }

std::any OpTernary::Evaluate(const Context &context, std::vector<std::string>* dumpEval) const {
    std::any condVal = Condition->Evaluate(context, dumpEval);
    bool cond = Utils::MakeBool(condVal);
    std::any result = cond
        ? TrueExpr->Evaluate(context, dumpEval)
        : FalseExpr->Evaluate(context, dumpEval);

    if (dumpEval) {
        dumpEval->push_back("Evaluated: " + Utils::FormatValue(condVal) + " ? " + (cond ? "..." : "(skipped)") +
                              " : " + (cond ? "(skipped)" : "...") + " = " + Utils::FormatValue(result));
    }
    return result;
}

std::string OpTernary::DumpStructure(int indent) const {
    std::string indentStr(indent * 2, ' ');
    return indentStr + "Ternary\n" +
           Condition->DumpStructure(indent + 1) +
           TrueExpr->DumpStructure(indent + 1) +
           FalseExpr->DumpStructure(indent + 1);
}

std::string OpTernary::Write() const {
    std::string condStr = Condition->Write();
    std::string trueStr = TrueExpr->Write();
    std::string falseStr = FalseExpr->Write();

    if (Condition->Precedence < this->Precedence)
        condStr = "(" + condStr + ")";
    if (TrueExpr->Precedence < this->Precedence)
        trueStr = "(" + trueStr + ")";
    if (FalseExpr->Precedence < this->Precedence)
        falseStr = "(" + falseStr + ")";

    return condStr + " ? " + trueStr + " : " + falseStr;
}

// ---------------------
// UnaryOp implementations
// ---------------------
UnaryOp::UnaryOp(const std::string &name, const std::string &op, std::shared_ptr<ExpressionNode> operand, int precedence)
    : ExpressionNode(name, precedence), Operand(operand), Op(op) {
        this->_specificity = operand->GetSpecificity();
    }

std::any UnaryOp::Evaluate(const Context &context, std::vector<std::string>* dumpEval) const {
    std::any val = Operand->Evaluate(context, dumpEval);
    std::any result = DoEval(val);
    if (dumpEval) {
        dumpEval->push_back("Evaluated: " + Op + " " + Utils::FormatValue(val) +
                              " = " + Utils::FormatValue(result));
    }
    return result;
}

std::string UnaryOp::DumpStructure(int indent) const {
    std::string indentStr(indent * 2, ' ');
    return indentStr + Name + "\n" + Operand->DumpStructure(indent + 1);
}

std::string UnaryOp::Write() const {
    std::string operandStr = Operand->Write();
    if (Operand->Precedence < this->Precedence)
        operandStr = "(" + operandStr + ")";
    return Op + " " + operandStr;
}

OpNegative::OpNegative(std::shared_ptr<ExpressionNode> operand)
    : UnaryOp("Negative", "-", operand, 90) {}

std::any OpNegative::DoEval(const std::any &val) const {
    return -Utils::MakeNumeric(val);
}

OpNot::OpNot(std::shared_ptr<ExpressionNode> operand)
    : UnaryOp("Not", "not", operand, 90) {}

std::any OpNot::DoEval(const std::any &val) const {
    return !Utils::MakeBool(val);
}

// ---------------------
// Literal node implementations
// ---------------------
LiteralBoolean::LiteralBoolean(bool val)
    : ExpressionNode("Boolean", 100), value(val) {}

std::any LiteralBoolean::Evaluate(const Context &context, std::vector<std::string>* dumpEval) const {
    if (dumpEval)
        dumpEval->push_back("Boolean: " + Utils::FormatBoolean(value));
    return value;
}

std::string LiteralBoolean::DumpStructure(int indent) const {
    std::string indentStr(indent * 2, ' ');
    return indentStr + "Boolean(" + Utils::FormatBoolean(value) + ")\n";
}

std::string LiteralBoolean::Write() const {
    return Utils::FormatBoolean(value);
}

LiteralNumber::LiteralNumber(const std::string &val)
    : ExpressionNode("Number", 100) {
    // Numeric literals in expression source are always '.'-decimal (fixed by
    // the tokenizer's grammar, independent of Writer::DecimalSeparator).
    // std::from_chars is locale-independent, unlike std::stod, so this can't
    // be thrown off by the host process's global C locale either.
    std::from_chars(val.data(), val.data() + val.size(), value);
}

std::any LiteralNumber::Evaluate(const Context &context, std::vector<std::string>* dumpEval) const {
    if (dumpEval)
        dumpEval->push_back("Number: " + Utils::FormatNumeric(value));
    return value;
}

std::string LiteralNumber::DumpStructure(int indent) const {
    std::string indentStr(indent * 2, ' ');
    return indentStr + "Number(" + Utils::FormatNumeric(value) + ")\n";
}

std::string LiteralNumber::Write() const {
    return Utils::FormatNumeric(value);
}

LiteralString::LiteralString(const std::string &val)
    : ExpressionNode("String", 100), value(val) {}

std::any LiteralString::Evaluate(const Context &context, std::vector<std::string>* dumpEval) const {
    if (dumpEval)
        dumpEval->push_back("String: " + Utils::FormatString(value));
    return value;
}

std::string LiteralString::DumpStructure(int indent) const {
    std::string indentStr(indent * 2, ' ');
    return indentStr + "String(" + Utils::FormatString(value) + ")\n";
}

std::string LiteralString::Write() const {
    return Utils::FormatString(value);
}

Variable::Variable(const std::string &name)
    : ExpressionNode("Variable", 100), name(name) {}

std::any Variable::Evaluate(const Context &context, std::vector<std::string>* dumpEval) const {
    auto it = context.find(name);
    if (it == context.end())
        throw std::runtime_error("Variable '" + name + "' not found in context.");
    std::any value = it->second;

    if (value.type() == typeid(const char*)) {
        value = std::string(std::any_cast<const char*>(value));
    }

    if (!(value.type() == typeid(int) || value.type() == typeid(double) ||
          value.type() == typeid(bool) || value.type() == typeid(std::string)))
        throw std::runtime_error("Variable '" + name + "' must return bool, string, or numeric.");
    if (dumpEval)
        dumpEval->push_back("Fetching variable: " + name + " -> " + Utils::FormatValue(value));
    return value;
}

std::string Variable::DumpStructure(int indent) const {
    std::string indentStr(indent * 2, ' ');
    return indentStr + "Variable(" + name + ")\n";
}

std::string Variable::Write() const {
    return name;
}

// ---------------------
// FunctionCall implementation
// ---------------------
FunctionCall::FunctionCall(const std::string &funcName, const std::vector<std::shared_ptr<ExpressionNode>> &args)
    : ExpressionNode("FunctionCall", 100), funcName(funcName), args(args) {}

std::any FunctionCall::Evaluate(const Context &context, std::vector<std::string>* dumpEval) const {
    auto it = context.find(funcName);
    if (it == context.end())
        throw std::runtime_error("Function '" + funcName + "' not found in context.");
    std::any funcObj = it->second;
    if (funcObj.type() != typeid(FunctionWrapper))
        throw std::runtime_error("Context entry for '" + funcName + "' is not a function.");
    FunctionWrapper wrapper = std::any_cast<FunctionWrapper>(funcObj);
    
    std::vector<std::any> argValues;
    for (const auto &arg : args) {
        argValues.push_back(arg->Evaluate(context, dumpEval));
    }
    
    if (wrapper.arity >= 0 && argValues.size() != static_cast<size_t>(wrapper.arity)) {
        std::string formattedArgs;
        for (const auto &val : argValues)
            formattedArgs += Utils::FormatValue(val) + ", ";
        if (!formattedArgs.empty())
            formattedArgs = formattedArgs.substr(0, formattedArgs.size() - 2);
        throw std::runtime_error("Function '" + funcName + "' does not support the provided arguments (" + formattedArgs + ").");
    }
    
    std::any result = wrapper.func(argValues);
    if (!(result.type() == typeid(int) || result.type() == typeid(double) ||
          result.type() == typeid(bool) || result.type() == typeid(std::string)))
        throw std::runtime_error("Function '" + funcName + "' must return bool, string, or numeric.");
    
    if (dumpEval) {
        std::string formattedArgs;
        for (const auto &val : argValues)
            formattedArgs += Utils::FormatValue(val) + ", ";
        if (!formattedArgs.empty())
            formattedArgs = formattedArgs.substr(0, formattedArgs.size() - 2);
        dumpEval->push_back("Called function: " + funcName + "(" + formattedArgs +
                              ") = " + Utils::FormatValue(result));
    }
    
    return result;
}

std::string FunctionCall::DumpStructure(int indent) const {
    std::string indentStr(indent * 2, ' ');
    std::string outStr = indentStr + "FunctionCall(" + funcName + ")\n";
    for (const auto &arg : args)
        outStr += arg->DumpStructure(indent + 1);
    return outStr;
}

std::string FunctionCall::Write() const {
    std::string argsStr;
    for (size_t i = 0; i < args.size(); ++i) {
        argsStr += args[i]->Write();
        if (i < args.size() - 1)
            argsStr += ", ";
    }
    return funcName + "(" + argsStr + ")";
}

} // namespace ExpressionParser