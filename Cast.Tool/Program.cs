using Spectre.Console.Cli;
using Cast.Tool.Commands;

var app = new CommandApp();

app.Configure(config =>
{
    config.SetApplicationName("cast");
    
    // Add refactoring commands
    config.AddCommand<RenameCommand>("rename")
        .WithDescription("Rename a symbol at the specified location");
        
    config.AddCommand<ExtractMethodCommand>("extract-method")
        .WithDescription("Extract a method from the selected code");
        
    config.AddCommand<AddUsingCommand>("add-using")
        .WithDescription("Add missing using statements");
        
    config.AddCommand<ConvertAutoPropertyCommand>("convert-auto-property")
        .WithDescription("Convert between auto property and full property");
        
    config.AddCommand<AddExplicitCastCommand>("add-explicit-cast")
        .WithDescription("Add explicit cast to an expression");
        
    config.AddCommand<AddAwaitCommand>("add-await")
        .WithDescription("Add await to an async call");
        
    config.AddCommand<AddConstructorParametersCommand>("add-constructor-params")
        .WithDescription("Add constructor parameters from class members");
        
    config.AddCommand<AddDebuggerDisplayCommand>("add-debugger-display")
        .WithDescription("Add DebuggerDisplay attribute to a class");
        
    config.AddCommand<AddFileHeaderCommand>("add-file-header")
        .WithDescription("Add a file header comment to the source file");
        
    config.AddCommand<AddNamedArgumentCommand>("add-named-argument")
        .WithDescription("Add named arguments to method calls");
        
    config.AddCommand<ConvertForLoopCommand>("convert-for-loop")
        .WithDescription("Convert between for and foreach loops");
        
    config.AddCommand<ChangeMethodSignatureCommand>("change-method-signature")
        .WithDescription("Change method signature (parameters and return type)");
        
    config.AddCommand<ConvertAnonymousTypeToClassCommand>("convert-anonymous-type")
        .WithDescription("Convert anonymous type to class");
        
    config.AddCommand<ConvertCastToAsExpressionCommand>("convert-cast-as")
        .WithDescription("Convert between cast and as expressions");
        
    config.AddCommand<ConvertGetMethodToPropertyCommand>("convert-get-method")
        .WithDescription("Convert between Get method and property");
        
    config.AddCommand<ConvertIfToSwitchCommand>("convert-if-switch")
        .WithDescription("Convert between if-else-if and switch statements");
        
    config.AddCommand<ConvertStringLiteralCommand>("convert-string-literal")
        .WithDescription("Convert between regular and verbatim string literals");
        
    config.AddCommand<UseExplicitTypeCommand>("use-explicit-type")
        .WithDescription("Use explicit type (replace var)");
        
    config.AddCommand<UseImplicitTypeCommand>("use-implicit-type")
        .WithDescription("Use implicit type (var)");
        
    config.AddCommand<IntroduceLocalVariableCommand>("introduce-local-variable")
        .WithDescription("Introduce local variable for expression");
        
    config.AddCommand<ConvertClassToRecordCommand>("convert-class-record")
        .WithDescription("Convert class to record");
        
    config.AddCommand<ConvertLocalFunctionToMethodCommand>("convert-local-function")
        .WithDescription("Convert local function to method");
        
    config.AddCommand<ConvertNumericLiteralCommand>("convert-numeric-literal")
        .WithDescription("Convert numeric literal between decimal, hexadecimal, and binary formats");
        
    config.AddCommand<ConvertStringFormatCommand>("convert-string-format")
        .WithDescription("Convert String.Format calls to interpolated strings");
        
    config.AddCommand<ConvertToInterpolatedStringCommand>("convert-to-interpolated")
        .WithDescription("Convert string concatenation to interpolated string");
        
    config.AddCommand<EncapsulateFieldCommand>("encapsulate-field")
        .WithDescription("Encapsulate field as property");
        
    config.AddCommand<GenerateDefaultConstructorCommand>("generate-default-constructor")
        .WithDescription("Generate default constructor for class or struct");
        
    config.AddCommand<MakeMemberStaticCommand>("make-member-static")
        .WithDescription("Make member static");
        
    config.AddCommand<InvertIfStatementCommand>("invert-if")
        .WithDescription("Invert if statement condition");
        
    config.AddCommand<IntroduceParameterCommand>("introduce-parameter")
        .WithDescription("Introduce parameter to method");
        
    config.AddCommand<IntroduceUsingStatementCommand>("introduce-using-statement")
        .WithDescription("Introduce using statement for disposable objects");
        
    config.AddCommand<GenerateParameterCommand>("generate-parameter")
        .WithDescription("Generate parameter for method");
        
    config.AddCommand<InlineTemporaryVariableCommand>("inline-temporary")
        .WithDescription("Inline temporary variable");
        
    config.AddCommand<ReverseForStatementCommand>("reverse-for")
        .WithDescription("Reverse for statement direction");
        
    config.AddCommand<MakeLocalFunctionStaticCommand>("make-local-function-static")
        .WithDescription("Make local function static");
        
    config.AddCommand<MoveDeclarationNearReferenceCommand>("move-declaration-near-reference")
        .WithDescription("Move variable declaration closer to its first use");
        
    config.AddCommand<UseLambdaExpressionCommand>("use-lambda-expression")
        .WithDescription("Convert between lambda expression and block body");
        
    config.AddCommand<SyncNamespaceWithFolderCommand>("sync-namespace")
        .WithDescription("Sync namespace with folder structure");
        
    config.AddCommand<InvertConditionalExpressionsCommand>("invert-conditional")
        .WithDescription("Invert conditional expressions and logical operators");
        
    config.AddCommand<SplitOrMergeIfStatementsCommand>("split-merge-if")
        .WithDescription("Split or merge if statements");
        
    config.AddCommand<WrapBinaryExpressionsCommand>("wrap-binary-expressions")
        .WithDescription("Wrap binary expressions with line breaks");
        
    config.AddCommand<GenerateComparisonOperatorsCommand>("generate-comparison-operators")
        .WithDescription("Generate comparison operators for class");
        
    config.AddCommand<ConvertTupleToStructCommand>("convert-tuple-struct")
        .WithDescription("Convert tuple to struct");
        
    config.AddCommand<ExtractBaseClassCommand>("extract-base-class")
        .WithDescription("Extract base class from existing class");
        
    config.AddCommand<ExtractInterfaceCommand>("extract-interface")
        .WithDescription("Extract interface from existing class");
        
    config.AddCommand<ExtractLocalFunctionCommand>("extract-local-function")
        .WithDescription("Extract local function from code block");
        
    config.AddCommand<ImplementInterfaceMembersExplicitCommand>("implement-interface-explicit")
        .WithDescription("Implement all interface members explicitly");
        
    config.AddCommand<ImplementInterfaceMembersImplicitCommand>("implement-interface-implicit")
        .WithDescription("Implement all interface members implicitly");
        
    config.AddCommand<InlineMethodCommand>("inline-method")
        .WithDescription("Inline a method by replacing its calls with the method body");
        
    config.AddCommand<MoveTypeToMatchingFileCommand>("move-type-to-file")
        .WithDescription("Move type to its own matching file");
        
    config.AddCommand<MoveTypeToNamespaceFolderCommand>("move-type-to-namespace")
        .WithDescription("Move type to namespace and corresponding folder");
        
    config.AddCommand<PullMembersUpCommand>("pull-members-up")
        .WithDescription("Pull members up to base type or interface");
        
    config.AddCommand<SyncTypeAndFileCommand>("sync-type-file")
        .WithDescription("Synchronize type name and file name");
        
    config.AddCommand<UseRecursivePatternsCommand>("use-recursive-patterns")
        .WithDescription("Convert to recursive patterns for advanced pattern matching");
        
    config.AddCommand<RemoveUnusedUsingsCommand>("remove-unused-usings")
        .WithDescription("Remove unused using statements from the file");
        
    config.AddCommand<SortUsingsCommand>("sort-usings")
        .WithDescription("Sort using statements alphabetically with optional System separation");
        
    // Analysis commands
    config.AddCommand<FindSymbolsCommand>("find-symbols")
        .WithDescription("Find symbols matching a pattern (including partial matches)");
        
    config.AddCommand<FindReferencesCommand>("find-references")
        .WithDescription("Find all references to a symbol at the specified location");
        
    config.AddCommand<FindUsagesCommand>("find-usages")
        .WithDescription("Find all usages of a symbol, type, or member");
        
    config.AddCommand<FindDependenciesCommand>("find-dependencies")
        .WithDescription("Find dependencies and create a dependency graph from a type");
        
    config.AddCommand<FindDuplicateCodeCommand>("find-duplicate-code")
        .WithDescription("Find code that is substantially similar to existing code");
});

return app.Run(args);
