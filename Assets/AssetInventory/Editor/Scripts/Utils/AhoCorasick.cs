using System.Collections.Generic;

namespace AssetInventory
{
    public class AhoCorasick
    {
        public class Node
        {
            public readonly Dictionary<byte, Node> Children = new Dictionary<byte, Node>();
            public Node Fail;
            public readonly List<int> Outputs = new List<int>();
        }

        private readonly Node _root = new Node();

        public AhoCorasick(IList<byte[]> patterns)
        {
            for (int i = 0; i < patterns.Count; i++)
            {
                byte[] pat = patterns[i];
                Node node = _root;
                foreach (byte b in pat)
                {
                    if (!node.Children.TryGetValue(b, out Node next))
                    {
                        next = new Node();
                        node.Children[b] = next;
                    }
                    node = next;
                }
                node.Outputs.Add(i);
            }

            Queue<Node> q = new Queue<Node>();
            foreach (Node child in _root.Children.Values)
            {
                child.Fail = _root;
                q.Enqueue(child);
            }

            while (q.Count > 0)
            {
                Node current = q.Dequeue();
                foreach (KeyValuePair<byte, Node> kv in current.Children)
                {
                    byte transition = kv.Key;
                    Node childNode = kv.Value;
                    Node failNode = current.Fail;
                    Node nextFail = null;

                    while (failNode != null && !failNode.Children.TryGetValue(transition, out nextFail))
                    {
                        failNode = failNode.Fail;
                    }

                    childNode.Fail = nextFail ?? _root;
                    childNode.Outputs.AddRange(childNode.Fail.Outputs);
                    q.Enqueue(childNode);
                }
            }
        }

        public void Scan(byte[] buffer, int length, HashSet<int> foundIds, ref Node state)
        {
            for (int i = 0; i < length; i++)
            {
                byte b = buffer[i];
                while (state != _root && !state.Children.ContainsKey(b))
                {
                    state = state.Fail;
                }
                if (state.Children.TryGetValue(b, out Node next))
                {
                    state = next;
                    foreach (int id in state.Outputs)
                    {
                        foundIds.Add(id);
                    }
                }
            }
        }

        public Node Root => _root;
    }
}