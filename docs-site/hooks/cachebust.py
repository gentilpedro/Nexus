"""Versão no endereço do CSS e do JS do site (issue #92).

O Nginx e o Cloudflare guardam /assets/ por 7 dias. Sem isto, uma folha de estilo nova só chega
ao leitor uma semana depois da publicação. Com `?v=<hash do conteúdo>`, cada mudança vira um
endereço novo e o cache antigo deixa de valer; o conteúdo que não mudou continua em cache.
"""

import hashlib
import os

_ARQUIVOS = (
    "assets/stylesheets/nexus.css",
    "assets/javascripts/instalador.js",
)
_hashes: dict[str, str] = {}


def _hash(docs_dir: str, relativo: str) -> str:
    if relativo not in _hashes:
        with open(os.path.join(docs_dir, relativo), "rb") as f:
            _hashes[relativo] = hashlib.sha256(f.read()).hexdigest()[:10]
    return _hashes[relativo]


def on_post_page(output: str, page, config) -> str:
    for relativo in _ARQUIVOS:
        output = output.replace(relativo + '"', f'{relativo}?v={_hash(config["docs_dir"], relativo)}"')
    return output
