using System; public interface ICalculator { int Add(int a, int b); } public class Calculator : ICalculator { public int Add(int a, int b) { return a + b; } }
