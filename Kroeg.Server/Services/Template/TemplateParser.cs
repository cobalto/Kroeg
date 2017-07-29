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
                if (command.StartsWith("if") || command.StartsWith("while"))
                {
                    entry.Type = command.Split(' ')[0];
                    ifStack.Push(result.Count);
                }
                else if (command.StartsWith("end"))
                {
                    entry.Type = "end";

                    var lastPos = ifStack.Pop();
                    var res = result[lastPos];
                    res.Offset = result.Count;
                    result[lastPos] = res;

                    entry.Offset = lastPos;
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
