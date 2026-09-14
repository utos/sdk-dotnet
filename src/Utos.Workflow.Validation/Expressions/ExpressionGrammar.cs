using System.Collections.Generic;
using Acornima;
using Acornima.Ast;

namespace Utos.Workflows.V1.Validation
{
    internal sealed class GrammarViolation
    {
        public GrammarViolation(string code, string message, int line, int column)
        {
            Code = code;
            Message = message;
            Line = line;
            Column = column;
        }

        public string Code { get; }
        public string Message { get; }
        public int Line { get; }
        public int Column { get; }
    }

    /// <summary>
    /// The grammar of <c>api/docs/template-expressions.md</c>: an allow-list of syntax-tree node
    /// types, where anything not named is refused — including whatever a future ECMAScript edition
    /// adds. Loose on data access, with no loops, classes, prototypes, or functions other than
    /// arrows, so that iteration can only happen over data that already exists and nothing can
    /// build a prototype chain.
    /// </summary>
    internal static class ExpressionGrammar
    {
        public static List<GrammarViolation> Check(Program program)
        {
            var violations = new List<GrammarViolation>();
            foreach (Statement statement in program.Body)
                CheckStatement(statement, false, violations);
            return violations;
        }

        // ---- statements -----------------------------------------------------------------------

        private static void CheckStatement(Statement node, bool inArrowBody, List<GrammarViolation> v)
        {
            switch (node)
            {
                case VariableDeclaration declaration:
                    if (declaration.Kind == VariableDeclarationKind.Var)
                        Reject(v, ValidationCodes.ExpressionVar, "use const or let", node);
                    foreach (VariableDeclarator declarator in declaration.Declarations)
                    {
                        CheckPattern(declarator.Id, inArrowBody, v);
                        if (declarator.Init != null) CheckExpression(declarator.Init, inArrowBody, v);
                    }
                    break;

                case ExpressionStatement statement:
                    CheckExpression(statement.Expression, inArrowBody, v);
                    break;

                case IfStatement ifStatement:
                    CheckExpression(ifStatement.Test, inArrowBody, v);
                    CheckStatement(ifStatement.Consequent, inArrowBody, v);
                    if (ifStatement.Alternate != null) CheckStatement(ifStatement.Alternate, inArrowBody, v);
                    break;

                case BlockStatement block:
                    foreach (Statement statement in block.Body) CheckStatement(statement, inArrowBody, v);
                    break;

                case EmptyStatement:
                    break;

                case ReturnStatement ret:
                    // Outside an arrow body a strict-mode parser has already refused it (E060).
                    if (ret.Argument != null) CheckExpression(ret.Argument, inArrowBody, v);
                    break;

                case ForStatement:
                case ForInStatement:
                case ForOfStatement:
                case WhileStatement:
                case DoWhileStatement:
                    Reject(v, ValidationCodes.ExpressionLoop,
                        "loops are not part of the language; iterate with map, filter, flatMap or reduce", node);
                    break;

                case FunctionDeclaration:
                    Reject(v, ValidationCodes.ExpressionFunction,
                        "function declarations are not part of the language; use a const arrow function", node);
                    break;

                case ClassDeclaration:
                    Reject(v, ValidationCodes.ExpressionClass, "classes are not part of the language", node);
                    break;

                case TryStatement:
                case ThrowStatement:
                case SwitchStatement:
                case LabeledStatement:
                case WithStatement:
                case DebuggerStatement:
                case BreakStatement:
                case ContinueStatement:
                    Reject(v, ValidationCodes.ExpressionStatement, node.Type + " is not part of the language", node);
                    break;

                default:
                    Reject(v, ValidationCodes.ExpressionUnknownNode, node.Type + " is not part of the language", node);
                    break;
            }
        }

        // ---- binding patterns -----------------------------------------------------------------

        private static void CheckPattern(Node node, bool inArrowBody, List<GrammarViolation> v)
        {
            switch (node)
            {
                case Identifier:
                    break;

                case ObjectPattern objectPattern:
                    foreach (Node item in objectPattern.Properties)
                    {
                        if (item is RestElement rest) CheckPattern(rest.Argument, inArrowBody, v);
                        else if (item is Property property)
                        {
                            if (property.Computed) CheckExpression(property.Key, inArrowBody, v);
                            CheckPattern(property.Value, inArrowBody, v);
                        }
                        else Reject(v, ValidationCodes.ExpressionUnknownNode, item.Type + " is not part of the language", item);
                    }
                    break;

                case ArrayPattern arrayPattern:
                    foreach (Node element in arrayPattern.Elements)
                        if (element != null) CheckPattern(element, inArrowBody, v);
                    break;

                case AssignmentPattern assignment:
                    CheckPattern(assignment.Left, inArrowBody, v);
                    CheckExpression(assignment.Right, inArrowBody, v);
                    break;

                case RestElement rest:
                    CheckPattern(rest.Argument, inArrowBody, v);
                    break;

                case MemberExpression member:
                    // An assignment target such as `o.n = 5`. Frozen inputs make it a TypeError at
                    // evaluation; locals accept it.
                    CheckExpression(member, inArrowBody, v);
                    break;

                default:
                    Reject(v, ValidationCodes.ExpressionUnknownNode, node.Type + " is not part of the language", node);
                    break;
            }
        }

        // ---- expressions ----------------------------------------------------------------------

        private static void CheckExpression(Node node, bool inArrowBody, List<GrammarViolation> v)
        {
            switch (node)
            {
                case Identifier:
                case Literal:
                    break;

                case TemplateLiteral template:
                    foreach (Expression expression in template.Expressions) CheckExpression(expression, inArrowBody, v);
                    break;

                case ArrayExpression array:
                    foreach (Expression element in array.Elements)
                    {
                        if (element == null)
                            Reject(v, ValidationCodes.ExpressionArrayHole, "array holes are not part of the language", array);
                        else if (element is SpreadElement spread)
                            CheckExpression(spread.Argument, inArrowBody, v);
                        else
                            CheckExpression(element, inArrowBody, v);
                    }
                    break;

                case ObjectExpression objectExpression:
                    foreach (Node item in objectExpression.Properties)
                    {
                        if (item is SpreadElement spread)
                        {
                            CheckExpression(spread.Argument, inArrowBody, v);
                            continue;
                        }

                        if (!(item is Property property))
                        {
                            Reject(v, ValidationCodes.ExpressionUnknownNode, item.Type + " is not part of the language", item);
                            continue;
                        }

                        if (property.Kind != PropertyKind.Init || property.Method)
                        {
                            // Refused as a whole; its function body is not a second violation.
                            Reject(v, ValidationCodes.ExpressionAccessor,
                                "getters, setters and methods are not part of the language", property);
                            continue;
                        }

                        string key = null;
                        if (!property.Computed)
                        {
                            if (property.Key is Identifier identifier) key = identifier.Name;
                            else if (property.Key is StringLiteral literal) key = literal.Value;
                        }
                        if (key == "__proto__")
                            Reject(v, ValidationCodes.ExpressionProto, "an object literal cannot set its prototype", property);

                        if (property.Computed) CheckExpression(property.Key, inArrowBody, v);
                        CheckExpression(property.Value, inArrowBody, v);
                    }
                    break;

                case MemberExpression member:
                    CheckExpression(member.Object, inArrowBody, v);
                    if (member.Computed) CheckExpression(member.Property, inArrowBody, v);
                    else if (!(member.Property is Identifier))
                        Reject(v, ValidationCodes.ExpressionUnknownNode, member.Property.Type + " is not part of the language", member.Property);
                    break;

                case ChainExpression chain:
                    CheckExpression(chain.Expression, inArrowBody, v);
                    break;

                case CallExpression call:
                    if (call.Callee is Identifier callee && IsForbiddenCallee(callee.Name))
                        Reject(v, ValidationCodes.ExpressionForbiddenCall, "`" + callee.Name + "()` cannot be called", node);
                    CheckExpression(call.Callee, inArrowBody, v);
                    CheckArguments(call.Arguments, inArrowBody, v);
                    break;

                case NewExpression newExpression:
                    if (!(newExpression.Callee is Identifier newCallee && (newCallee.Name == "Set" || newCallee.Name == "Map")))
                        Reject(v, ValidationCodes.ExpressionNew, "`new` is limited to Set and Map", node);
                    CheckArguments(newExpression.Arguments, inArrowBody, v);
                    break;

                case ConditionalExpression conditional:
                    CheckExpression(conditional.Test, inArrowBody, v);
                    CheckExpression(conditional.Consequent, inArrowBody, v);
                    CheckExpression(conditional.Alternate, inArrowBody, v);
                    break;

                case LogicalExpression logical:
                    CheckExpression(logical.Left, inArrowBody, v);
                    CheckExpression(logical.Right, inArrowBody, v);
                    break;

                case BinaryExpression binary:
                    if (!IsAllowedBinary(binary.Operator))
                        Reject(v, ValidationCodes.ExpressionBinaryOperator,
                            "operator `" + binary.Operator + "` is not part of the language", node);
                    CheckExpression(binary.Left, inArrowBody, v);
                    CheckExpression(binary.Right, inArrowBody, v);
                    break;

                case AssignmentExpression assignment:
                    if (!IsAllowedAssignment(assignment.Operator))
                        Reject(v, ValidationCodes.ExpressionAssignmentOperator,
                            "operator `" + assignment.Operator + "` is not part of the language", node);
                    CheckPattern(assignment.Left, inArrowBody, v);
                    CheckExpression(assignment.Right, inArrowBody, v);
                    break;

                // Before UnaryExpression: Acornima models `++`/`--` as a subclass of it.
                case UpdateExpression update:
                    CheckExpression(update.Argument, inArrowBody, v);
                    break;

                case UnaryExpression unary:
                    if (!IsAllowedUnary(unary.Operator))
                        Reject(v, ValidationCodes.ExpressionUnaryOperator,
                            "operator `" + unary.Operator + "` is not part of the language", node);
                    CheckExpression(unary.Argument, inArrowBody, v);
                    break;

                case ArrowFunctionExpression arrow:
                    if (arrow.Async)
                        Reject(v, ValidationCodes.ExpressionAsync, "async arrow functions are not part of the language", node);
                    foreach (Node parameter in arrow.Params) CheckPattern(parameter, true, v);
                    if (arrow.Body is BlockStatement block)
                        foreach (Statement statement in block.Body) CheckStatement(statement, true, v);
                    else
                        CheckExpression(arrow.Body, true, v);
                    break;

                // Named refusals, so the message says what to use instead.
                case FunctionExpression:
                    Reject(v, ValidationCodes.ExpressionFunction,
                        "function expressions are not part of the language; use an arrow function", node);
                    break;
                case ClassExpression:
                    Reject(v, ValidationCodes.ExpressionClass, "classes are not part of the language", node);
                    break;
                case ThisExpression:
                    Reject(v, ValidationCodes.ExpressionThis, "`this` is not part of the language", node);
                    break;
                case YieldExpression:
                case AwaitExpression:
                    Reject(v, ValidationCodes.ExpressionAsync, "async and generator constructs are not part of the language", node);
                    break;
                case ImportExpression:
                case MetaProperty:
                    Reject(v, ValidationCodes.ExpressionModule, "modules are not part of the language", node);
                    break;
                case SequenceExpression:
                    Reject(v, ValidationCodes.ExpressionSequence, "comma expressions are not part of the language", node);
                    break;

                default:
                    Reject(v, ValidationCodes.ExpressionUnknownNode, node.Type + " is not part of the language", node);
                    break;
            }
        }

        private static void CheckArguments(in NodeList<Expression> arguments, bool inArrowBody, List<GrammarViolation> v)
        {
            foreach (Expression argument in arguments)
            {
                if (argument is SpreadElement spread) CheckExpression(spread.Argument, inArrowBody, v);
                else CheckExpression(argument, inArrowBody, v);
            }
        }

        private static bool IsForbiddenCallee(string name) =>
            name == "Array" || name == "Object" || name == "Function" || name == "eval";

        private static bool IsAllowedBinary(Operator op)
        {
            switch (op)
            {
                case Operator.StrictEquality:
                case Operator.StrictInequality:
                case Operator.Equality:
                case Operator.Inequality:
                case Operator.LessThan:
                case Operator.LessThanOrEqual:
                case Operator.GreaterThan:
                case Operator.GreaterThanOrEqual:
                case Operator.Addition:
                case Operator.Subtraction:
                case Operator.Multiplication:
                case Operator.Division:
                case Operator.Remainder:
                case Operator.Exponentiation:
                case Operator.In:
                    return true;
                default:
                    return false;
            }
        }

        private static bool IsAllowedAssignment(Operator op)
        {
            switch (op)
            {
                case Operator.Assignment:
                case Operator.AdditionAssignment:
                case Operator.SubtractionAssignment:
                case Operator.MultiplicationAssignment:
                case Operator.DivisionAssignment:
                    return true;
                default:
                    return false;
            }
        }

        private static bool IsAllowedUnary(Operator op)
        {
            switch (op)
            {
                case Operator.LogicalNot:
                case Operator.UnaryNegation:
                case Operator.UnaryPlus:
                case Operator.TypeOf:
                    return true;
                default:
                    return false;
            }
        }

        private static void Reject(List<GrammarViolation> v, string code, string message, Node node) =>
            v.Add(new GrammarViolation(code, message, node.Location.Start.Line, node.Location.Start.Column));
    }
}
