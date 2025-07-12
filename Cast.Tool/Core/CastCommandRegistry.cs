using Cast.Tool.Commands;

namespace Cast.Tool.Core;

/// <summary>
/// Central registry for all Cast commands and their descriptions
/// </summary>
public static class CastCommandRegistry
{
    /// <summary>
    /// Registry of all Cast commands with their types, names, and descriptions
    /// </summary>
    public static readonly Dictionary<string, (Type CommandType, string Description)> Commands = new()
    {
        ["rename"] = (typeof(RenameCommand), "Rename a symbol at the specified location"),
        ["extract-method"] = (typeof(ExtractMethodCommand), "Extract a method from the selected code"),
        ["add-using"] = (typeof(AddUsingCommand), "Add missing using statements"),
        ["convert-auto-property"] = (typeof(ConvertAutoPropertyCommand), "Convert between auto property and full property"),
        ["add-explicit-cast"] = (typeof(AddExplicitCastCommand), "Add explicit cast to an expression"),
        ["add-await"] = (typeof(AddAwaitCommand), "Add await to an async call"),
        ["add-constructor-params"] = (typeof(AddConstructorParametersCommand), "Add constructor parameters from class members"),
        ["add-debugger-display"] = (typeof(AddDebuggerDisplayCommand), "Add DebuggerDisplay attribute to a class"),
        ["add-file-header"] = (typeof(AddFileHeaderCommand), "Add a file header comment to the source file"),
        ["add-named-argument"] = (typeof(AddNamedArgumentCommand), "Add named arguments to method calls"),
        ["convert-for-loop"] = (typeof(ConvertForLoopCommand), "Convert between for and foreach loops"),
        ["change-method-signature"] = (typeof(ChangeMethodSignatureCommand), "Change method signature (parameters and return type)"),
        ["convert-anonymous-type"] = (typeof(ConvertAnonymousTypeToClassCommand), "Convert anonymous type to class"),
        ["convert-cast-as"] = (typeof(ConvertCastToAsExpressionCommand), "Convert between cast and as expressions"),
        ["convert-get-method"] = (typeof(ConvertGetMethodToPropertyCommand), "Convert between Get method and property"),
        ["convert-if-switch"] = (typeof(ConvertIfToSwitchCommand), "Convert between if-else-if and switch statements"),
        ["convert-string-literal"] = (typeof(ConvertStringLiteralCommand), "Convert between regular and verbatim string literals"),
        ["use-explicit-type"] = (typeof(UseExplicitTypeCommand), "Use explicit type (replace var)"),
        ["use-implicit-type"] = (typeof(UseImplicitTypeCommand), "Use implicit type (var)"),
        ["introduce-local-variable"] = (typeof(IntroduceLocalVariableCommand), "Introduce local variable for expression"),
        ["convert-class-record"] = (typeof(ConvertClassToRecordCommand), "Convert class to record"),
        ["convert-local-function"] = (typeof(ConvertLocalFunctionToMethodCommand), "Convert local function to method"),
        ["convert-numeric-literal"] = (typeof(ConvertNumericLiteralCommand), "Convert numeric literal between decimal, hexadecimal, and binary formats"),
        ["convert-string-format"] = (typeof(ConvertStringFormatCommand), "Convert String.Format calls to interpolated strings"),
        ["convert-to-interpolated"] = (typeof(ConvertToInterpolatedStringCommand), "Convert string concatenation to interpolated string"),
        ["encapsulate-field"] = (typeof(EncapsulateFieldCommand), "Encapsulate field as property"),
        ["generate-default-constructor"] = (typeof(GenerateDefaultConstructorCommand), "Generate default constructor for class or struct"),
        ["make-member-static"] = (typeof(MakeMemberStaticCommand), "Make member static"),
        ["invert-if"] = (typeof(InvertIfStatementCommand), "Invert if statement condition"),
        ["introduce-parameter"] = (typeof(IntroduceParameterCommand), "Introduce parameter to method"),
        ["introduce-using-statement"] = (typeof(IntroduceUsingStatementCommand), "Introduce using statement for disposable objects"),
        ["generate-parameter"] = (typeof(GenerateParameterCommand), "Generate parameter for method"),
        ["inline-temporary"] = (typeof(InlineTemporaryVariableCommand), "Inline temporary variable"),
        ["reverse-for"] = (typeof(ReverseForStatementCommand), "Reverse for statement direction"),
        ["make-local-function-static"] = (typeof(MakeLocalFunctionStaticCommand), "Make local function static"),
        ["move-declaration-near-reference"] = (typeof(MoveDeclarationNearReferenceCommand), "Move variable declaration closer to its first use"),
        ["use-lambda-expression"] = (typeof(UseLambdaExpressionCommand), "Convert between lambda expression and block body"),
        ["sync-namespace"] = (typeof(SyncNamespaceWithFolderCommand), "Sync namespace with folder structure"),
        ["invert-conditional"] = (typeof(InvertConditionalExpressionsCommand), "Invert conditional expressions and logical operators"),
        ["split-merge-if"] = (typeof(SplitOrMergeIfStatementsCommand), "Split or merge if statements"),
        ["wrap-binary-expressions"] = (typeof(WrapBinaryExpressionsCommand), "Wrap binary expressions with line breaks"),
        ["generate-comparison-operators"] = (typeof(GenerateComparisonOperatorsCommand), "Generate comparison operators for class"),
        ["convert-tuple-struct"] = (typeof(ConvertTupleToStructCommand), "Convert tuple to struct"),
        ["extract-base-class"] = (typeof(ExtractBaseClassCommand), "Extract base class from existing class"),
        ["extract-interface"] = (typeof(ExtractInterfaceCommand), "Extract interface from existing class"),
        ["extract-local-function"] = (typeof(ExtractLocalFunctionCommand), "Extract local function from code block"),
        ["implement-interface-explicit"] = (typeof(ImplementInterfaceMembersExplicitCommand), "Implement all interface members explicitly"),
        ["implement-interface-implicit"] = (typeof(ImplementInterfaceMembersImplicitCommand), "Implement all interface members implicitly"),
        ["inline-method"] = (typeof(InlineMethodCommand), "Inline a method by replacing its calls with the method body"),
        ["move-type-to-file"] = (typeof(MoveTypeToMatchingFileCommand), "Move type to its own matching file"),
        ["move-type-to-namespace"] = (typeof(MoveTypeToNamespaceFolderCommand), "Move type to namespace and corresponding folder"),
        ["pull-members-up"] = (typeof(PullMembersUpCommand), "Pull members up to base type or interface"),
        ["sync-type-file"] = (typeof(SyncTypeAndFileCommand), "Synchronize type name and file name"),
        ["use-recursive-patterns"] = (typeof(UseRecursivePatternsCommand), "Convert to recursive patterns for advanced pattern matching"),
        ["remove-unused-usings"] = (typeof(RemoveUnusedUsingsCommand), "Remove unused using statements from the file"),
        ["sort-usings"] = (typeof(SortUsingsCommand), "Sort using statements alphabetically with optional System separation"),
        ["find-symbols"] = (typeof(FindSymbolsCommand), "Find symbols matching a pattern (including partial matches)"),
        ["find-references"] = (typeof(FindReferencesCommand), "Find all references to a symbol at the specified location"),
        ["find-usages"] = (typeof(FindUsagesCommand), "Find all usages of a symbol, type, or member"),
        ["find-dependencies"] = (typeof(FindDependenciesCommand), "Find dependencies and create a dependency graph from a type"),
        ["find-duplicate-code"] = (typeof(FindDuplicateCodeCommand), "Find code that is substantially similar to existing code")
    };
}