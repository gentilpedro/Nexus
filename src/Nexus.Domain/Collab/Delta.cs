namespace Nexus.Domain.Collab;

/// <summary>
/// Sequência de operações no formato Delta do Quill: tanto um documento (só inserts) quanto uma
/// alteração sobre um documento (retain, insert, delete).
/// </summary>
/// <remarks>
/// <para>
/// Porte fiel do <c>quill-delta</c> 5.1.0, a versão embutida no Quill 2.0.3 que roda no navegador.
/// Fiel inclusive na normalização (<see cref="Push"/>) e nas otimizações de <see cref="Compose"/>:
/// o servidor compõe as operações que os editores mandam, e qualquer diferença de resultado entre
/// as duas implementações faria o documento salvo divergir do que as pessoas estão vendo. Os
/// vetores em <c>tests/Nexus.Domain.Tests/Collab/delta-vectors.json</c>, gerados do pacote
/// oficial, fixam essa equivalência.
/// </para>
/// <para>
/// A instância é mutável só durante a construção (<see cref="Insert(string, AttributeMap?)"/>,
/// <see cref="Retain"/>, <see cref="Delete"/>), como no original. <see cref="Compose"/> e
/// <see cref="Transform"/> sempre devolvem uma instância nova.
/// </para>
/// </remarks>
public sealed class Delta
{
    private readonly List<DeltaOp> ops;

    public Delta() => ops = [];

    /// <summary>Usa as operações como vieram, sem normalizar (igual ao construtor do original).</summary>
    public Delta(IEnumerable<DeltaOp> ops) => this.ops = ops.ToList();

    public IReadOnlyList<DeltaOp> Ops => ops;

    public Delta Insert(string text, AttributeMap? attributes = null) =>
        text.Length == 0 ? this : Push(DeltaOp.InsertText(text, attributes));

    public Delta Insert(DeltaEmbed embed, AttributeMap? attributes = null) =>
        Push(DeltaOp.InsertEmbed(embed, attributes));

    public Delta Delete(int length) => length <= 0 ? this : Push(DeltaOp.Delete(length));

    public Delta Retain(int length, AttributeMap? attributes = null) =>
        length <= 0 ? this : Push(DeltaOp.Retain(length, attributes));

    /// <summary>
    /// Acrescenta uma operação mantendo a forma canônica: apaga seguidos viram um só, texto ou
    /// retain seguidos com os mesmos atributos se juntam, e insert logo depois de delete vai para
    /// antes dele (a ordem entre os dois não muda o resultado, então fixa-se uma).
    /// </summary>
    public Delta Push(DeltaOp newOp)
    {
        var index = ops.Count;
        var lastOp = index > 0 ? ops[index - 1] : null;
        if (lastOp is not null)
        {
            if (newOp.IsDelete && lastOp.IsDelete)
            {
                ops[index - 1] = DeltaOp.Delete(lastOp.Count + newOp.Count);
                return this;
            }

            if (lastOp.IsDelete && newOp.IsInsert)
            {
                index -= 1;
                lastOp = index > 0 ? ops[index - 1] : null;
                if (lastOp is null)
                {
                    ops.Insert(0, newOp);
                    return this;
                }
            }

            if (AttributeMap.AreEqual(newOp.Attributes, lastOp.Attributes))
            {
                if (newOp.IsTextInsert && lastOp.IsTextInsert)
                {
                    ops[index - 1] = DeltaOp.InsertText(lastOp.Text + newOp.Text, newOp.Attributes);
                    return this;
                }
                if (newOp.IsRetain && lastOp.IsRetain)
                {
                    ops[index - 1] = DeltaOp.Retain(lastOp.Count + newOp.Count, newOp.Attributes);
                    return this;
                }
            }
        }

        if (index == ops.Count)
        {
            ops.Add(newOp);
        }
        else
        {
            ops.Insert(index, newOp);
        }
        return this;
    }

    /// <summary>Remove o retain sem atributos do fim, que não altera nada.</summary>
    public Delta Chop()
    {
        if (ops.Count > 0 && ops[^1] is { IsRetain: true, Attributes: null })
        {
            ops.RemoveAt(ops.Count - 1);
        }
        return this;
    }

    /// <summary>Tamanho total: para um documento, o número de posições do texto.</summary>
    public int Length() => ops.Sum(op => op.Length);

    /// <summary>
    /// Quantas posições do documento de origem a alteração percorre (retain + delete). Uma
    /// alteração só é aplicável a documentos com pelo menos esse tamanho.
    /// </summary>
    public int BaseLength() => ops.Where(op => !op.IsInsert).Sum(op => op.Count);

    /// <summary>Documento é um Delta só de inserts.</summary>
    public bool IsDocument() => ops.All(op => op.IsInsert);

    public Delta Concat(Delta other)
    {
        var delta = new Delta(ops);
        if (other.ops.Count > 0)
        {
            delta.Push(other.ops[0]);
            delta.ops.AddRange(other.ops.Skip(1));
        }
        return delta;
    }

    /// <summary>
    /// Esta alteração seguida de <paramref name="other"/>, como uma só. Aplicada a um documento,
    /// devolve o documento resultante.
    /// </summary>
    public Delta Compose(Delta other)
    {
        var thisIter = new OpIterator(ops);
        var otherIter = new OpIterator(other.ops);
        var prefix = new List<DeltaOp>();

        // Atalho do original: um retain simples no começo de "other" deixa passar os inserts de
        // "this" sem compará-los um a um.
        var firstOther = otherIter.Peek();
        if (firstOther is { IsRetain: true, Attributes: null })
        {
            var firstLeft = firstOther.Count;
            while (thisIter.PeekType() == DeltaOpKind.Insert && thisIter.PeekLength() <= firstLeft)
            {
                firstLeft -= thisIter.PeekLength();
                prefix.Add(thisIter.Next());
            }
            if (firstOther.Count - firstLeft > 0)
            {
                otherIter.Next(firstOther.Count - firstLeft);
            }
        }

        var delta = new Delta(prefix);
        while (thisIter.HasNext() || otherIter.HasNext())
        {
            if (otherIter.PeekType() == DeltaOpKind.Insert)
            {
                delta.Push(otherIter.Next());
            }
            else if (thisIter.PeekType() == DeltaOpKind.Delete)
            {
                delta.Push(thisIter.Next());
            }
            else
            {
                var length = Math.Min(thisIter.PeekLength(), otherIter.PeekLength());
                var thisOp = thisIter.Next(length);
                var otherOp = otherIter.Next(length);
                if (otherOp.IsRetain)
                {
                    // O null precisa sobreviver enquanto o resultado ainda for um retain; num
                    // insert, "remover formato" é simplesmente não ter o formato.
                    var attributes = AttributeMap.Compose(thisOp.Attributes, otherOp.Attributes, keepNull: thisOp.IsRetain);
                    var newOp = thisOp.IsRetain
                        ? DeltaOp.Retain(length, attributes)
                        : thisOp.WithAttributes(attributes);
                    delta.Push(newOp);

                    // Se o resto de "other" é só retain, o resto de "this" passa inteiro.
                    if (!otherIter.HasNext() && DeltaOp.AreEqual(delta.ops[^1], newOp))
                    {
                        var rest = new Delta(thisIter.Rest());
                        return delta.Concat(rest).Chop();
                    }
                }
                else if (otherOp.IsDelete && thisOp.IsRetain)
                {
                    delta.Push(otherOp);
                }
                // insert seguido de delete: os dois se anulam.
            }
        }
        return delta.Chop();
    }

    /// <summary>
    /// Ajusta <paramref name="other"/>, feita em paralelo a esta alteração sobre o mesmo
    /// documento, para ser aplicada depois desta.
    /// </summary>
    /// <param name="priority">
    /// <c>true</c> quando esta alteração é considerada a primeira: onde as duas inserem na mesma
    /// posição, o texto desta fica antes. O servidor define a ordem, então quem já foi aceito
    /// recebe a prioridade.
    /// </param>
    public Delta Transform(Delta other, bool priority)
    {
        var thisIter = new OpIterator(ops);
        var otherIter = new OpIterator(other.ops);
        var delta = new Delta();
        while (thisIter.HasNext() || otherIter.HasNext())
        {
            if (thisIter.PeekType() == DeltaOpKind.Insert
                && (priority || otherIter.PeekType() != DeltaOpKind.Insert))
            {
                delta.Retain(thisIter.Next().Length);
            }
            else if (otherIter.PeekType() == DeltaOpKind.Insert)
            {
                delta.Push(otherIter.Next());
            }
            else
            {
                var length = Math.Min(thisIter.PeekLength(), otherIter.PeekLength());
                var thisOp = thisIter.Next(length);
                var otherOp = otherIter.Next(length);
                if (thisOp.IsDelete)
                {
                    // O nosso delete já removeu o trecho: o retain ou delete deles perde o alvo.
                    continue;
                }
                if (otherOp.IsDelete)
                {
                    delta.Push(otherOp);
                }
                else
                {
                    delta.Retain(length, AttributeMap.Transform(thisOp.Attributes, otherOp.Attributes, priority));
                }
            }
        }
        return delta.Chop();
    }

    /// <summary>Percorre as operações pedaço a pedaço (porte do <c>OpIterator</c> original).</summary>
    private sealed class OpIterator(List<DeltaOp> ops)
    {
        private int index;
        private int offset;

        public bool HasNext() => PeekLength() < DeltaOp.Unbounded;

        public DeltaOp? Peek() => index < ops.Count ? ops[index] : null;

        public int PeekLength() => index < ops.Count ? ops[index].Length - offset : DeltaOp.Unbounded;

        public DeltaOpKind PeekType() => index < ops.Count ? ops[index].Kind : DeltaOpKind.Retain;

        public DeltaOp Next(int length = DeltaOp.Unbounded)
        {
            if (length <= 0)
            {
                length = DeltaOp.Unbounded;
            }
            if (index >= ops.Count)
            {
                return DeltaOp.Retain(DeltaOp.Unbounded);
            }

            var nextOp = ops[index];
            var currentOffset = offset;
            var opLength = nextOp.Length;
            if (length >= opLength - currentOffset)
            {
                length = opLength - currentOffset;
                index += 1;
                offset = 0;
            }
            else
            {
                offset += length;
            }

            return nextOp.Kind switch
            {
                DeltaOpKind.Delete => DeltaOp.Delete(length),
                DeltaOpKind.Retain => DeltaOp.Retain(length, nextOp.Attributes),
                _ when nextOp.Text is not null => DeltaOp.InsertText(nextOp.Text.Substring(currentOffset, length), nextOp.Attributes),
                _ => nextOp,
            };
        }

        public List<DeltaOp> Rest()
        {
            if (!HasNext())
            {
                return [];
            }
            if (offset == 0)
            {
                return ops.GetRange(index, ops.Count - index);
            }
            var savedOffset = offset;
            var savedIndex = index;
            var next = Next();
            var rest = ops.GetRange(index, ops.Count - index);
            offset = savedOffset;
            index = savedIndex;
            return [next, .. rest];
        }
    }
}
