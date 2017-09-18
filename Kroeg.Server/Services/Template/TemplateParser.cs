using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Kroeg.Server.Services.Template
{
    public struct TemplateItem
    {
        public string Type { get; set; }
        public string Data { get; set; }
        public int Offset { get; set; }
    }

    public class TemplateParser
    {
        public static List<TemplateItem> Parse(string template)
        {
            int start = 0;
            var result = new List<TemplateItem>();
            var ifStack = new Stack<int>();
            do
            {
                int nextCommand = template.IndexOf("{{", start);
                if (nextCommand < 0) break;

                if (start != nextCommand)
                {
                    var text = template.Substring(start, nextCommand - start);
                    result.Add(new TemplateItem { Type = "text", Data = text });
                }

                start = nextCommand;

                var end = template.IndexOf("}}", start);
                if (end < 0) throw new InvalidOperationException("Could not find end of template command!");

                var command = template.Substring(start + 2, end - start - 2);
                var entry = new TemplateItem { Type = "command", Data = command };
                // if: { type: if, data: command, offset: location after the else }
                // else: { type: jump, data: null, offset: location of the end }
                // end: { type: end, data: null, offset: location of the else/if?? }
                // {{if }} -> push location on ifstack
                // {{else }} -> pop, set if offset to here, add jump to ifstack
                // {{end }} -> pop, set jump offset to here.
                // {{elif }} -> pop, add jump to ifstack, set if offset to here
                if (command.StartsWith("if") || command.StartsWith("while"))
                {
                    entry.Type = command.Split(' ')[0];
                    ifStack.Push(-1);
                    ifStack.Push(result.Count);
                }
                else if (command.StartsWith("else"))
                {
                    entry.Type = "jump";

                    var lastPos = ifStack.Pop();
                    var res = result[lastPos];
                    res.Offset = result.Count + 1;
                    result[lastPos] = res;

                    ifStack.Push(result.Count);
                }
                else if (command.StartsWith("end"))
                {
                    entry.Type = "end";

                    while (ifStack.Peek() != -1)
                    {
                        var lastPos = ifStack.Pop();
                        var res = result[lastPos];
                        
                        if (res.Type == "while")
                        {
                            entry.Type = "jump";
                            entry.Offset = lastPos;
                        }
                        
                        res.Offset = result.Count + 1;
                        result[lastPos] = res;
                    }

                    ifStack.Pop();
                }
                else if (command.StartsWith("wrap"))
                {
                    entry.Type = "wrap";
                    entry.Data = entry.Data.Substring(5);
                }
                else if (command.StartsWith("elif"))
                {
                    var jump = new TemplateItem { Type = "jump" };

                    var lastPos = ifStack.Pop();

                    var res = result[lastPos];
                    res.Offset = result.Count + 1;
                    result[lastPos] = res;
                    ifStack.Push(result.Count);
                    result.Add(jump);
                    
                    entry.Type = "if";
                    ifStack.Push(result.Count);
                }

                result.Add(entry);

                start = end + 2;
            } while (true);

            if (start < template.Length)
            {
                result.Add(new TemplateItem { Type = "text", Data = template.Substring(start) });
            }

            return result;
        }
    }
}
