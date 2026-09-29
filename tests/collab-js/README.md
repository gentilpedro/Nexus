# collab-js

Ferramentas em Node para a edição colaborativa dos Docs (#32).

O servidor compõe e valida as alterações em C# (`src/Nexus.Domain/Collab`), e o navegador compõe
e transforma com o `quill-delta` embutido no Quill 2.0.3. As duas implementações precisam dar
exatamente o mesmo resultado. Este pacote usa o `quill-delta@5.1.0` oficial como referência.

```bash
cd tests/collab-js
npm install
npm run vectors   # regenera tests/Nexus.Domain.Tests/Collab/delta-vectors.json
npm test          # testes do núcleo do cliente colaborativo
```

A semente dos vetores é fixa, então o arquivo gerado só muda se o gerador mudar. Os testes em C#
(`DeltaVectorTests`) leem o arquivo commitado e rodam na CI sem precisar de Node.
