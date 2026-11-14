# Novidades

O que mudou em cada versão do Yata. As notas completas (instalação, como conferir o arquivo e limitações conhecidas) ficam nas [versões do GitHub](https://github.com/akaneni/yata/releases).

## 1.0.0 — 2026-11-14

Primeira versão de [ShamilJafarov20/yata](https://github.com/ShamilJafarov20/yata), sobre a 0.7.1: temas (Liquid Glass entre eles), companheiros em pixel ao lado do relógio, páginas de timers, área de transferência, notas e dispositivos, selos no notch fechado e atalhos configuráveis. Detalhes no [CHANGELOG.md](CHANGELOG.md).

## 0.7.1 — 2026-10-29

- **O ponto do microfone não fica mais preso.** Às vezes o Windows não avisa que um app parou de usar o microfone, e o ponto laranja ficava aceso sem ninguém gravando. Enquanto algum app aparece, o Yata confere de novo a cada poucos segundos.
- **Relógio no centro.** Com o ponto da câmera ou do microfone aceso, o relógio ia alguns pixels para a direita; agora o notch cresce igual dos dois lados.

[Notas completas](https://github.com/akaneni/yata/releases/tag/v0.7.1)

## 0.7.0 — 2026-10-28

- **Dá para ver qual agente é.** Claude, Cursor e Codex aparecem com o próprio logo no aviso do notch, na bolha e na lista de atividades.
- **Selo de estado.** Concluído, falhou e precisa de você viram um selo pequeno (✓, ✗, !) no canto do ícone, em vez de substituí-lo.
- **A bolha virou pílula.** O ícone de origem numa ponta e o anel, os minutos ou o selo na outra, também para reunião e foco.
- **Título mais claro.** A atividade de um agente leva o nome do programa e do projeto, como `Orca · yata`.
- **IA na política de privacidade.** Uma seção nova diz com todas as letras: o Yata não usa nenhum modelo ou serviço de IA, e os dados do Google nunca vão para um.

[Notas completas](https://github.com/akaneni/yata/releases/tag/v0.7.0)

## 0.6.0 — 2026-10-28

- **Mais fluido.** O notch abre, troca de aba e fecha na frequência da sua tela: num monitor de 120 Hz, abrir passou de uns 36 para 115 quadros por segundo, com a mesma memória. Fechar é mais rápido que abrir, e o notch não fica mais vazio no meio da transição.
- **Movimento sóbrio.** **Configurações → Geral → Movimento sóbrio** abre e fecha o notch sem o balanço da mola.
- **A mesma largura em todas as abas.** Trocar de aba só muda a altura, e entre abas da mesma altura o notch fica parado enquanto a página desliza. A letra foi para baixo do player, em vez de alargar o notch.
- **Uma aba Foco mais completa.** Escolha a tarefa da sessão entre as da aba Tarefas e marque como feita ali mesmo. Ao lado, o foco de hoje contra uma meta diária (2 h por padrão, em **Configurações → Foco**) e os últimos 7 dias. A linha de status diz quando vem a pausa e, com o Google Agenda conectado, quantas sessões cabem até a próxima reunião.
- **Acabamento.** Espaçamentos iguais em todas as abas, botões que acendem suave ao passar o mouse e afundam um pouco no clique, e a Prateleira em colunas iguais.
- **No dia a dia:**
  - trocar de fuso numa viagem ajusta o relógio e os lembretes na hora;
  - uma sessão de foco pausa enquanto o PC dorme, em vez de contar o sono como foco;
  - a agenda e o Google Tasks se atualizam quando o PC acorda ou a rede volta;
  - o microfone em uso conta como você presente, então uma chamada não faz o Yata achar que você saiu;
  - um arquivo da Prateleira numa unidade ou servidor que não responde fica, apagado, em vez de sumir;
  - um `data.json` editado à mão ou em parte danificado é corrigido ao abrir, em vez de impedir o Yata de iniciar.

[Notas completas](https://github.com/akaneni/yata/releases/tag/v0.6.0)

## 0.5.0 — 2026-10-28

- **Fora das suas chamadas.** O Teams, o Meet, o Zoom, o OBS e os prints não capturam o notch, e você continua vendo: a próxima reunião, as tarefas e o projeto do agente não aparecem para os outros. Vem ligado e pode ser desligado em **Configurações → Geral**.
- **O som vem com o lembrete.** Um lembrete que esperou o jogo em tela cheia toca quando aparece, não no meio da partida. Com o microfone em uso, ou com o Windows pedindo para não perturbar, os lembretes chegam sem som. E dá para desligar o som em **Configurações → Avisos no notch**.
- **Avisos que não chegam atrasados.** Um lembrete que esperou e deixou de valer, como "Daily em 5 min" depois que a reunião começou, é descartado.
- **Boas-vindas.** Na primeira vez, as Configurações abrem com o que dá para fazer no notch e as escolhas que são suas. O lembrete de água passa a vir desligado em instalações novas, e o menu da bandeja ganhou **Abrir o Yata**.
- **Notch de atividades.** Com o notch aberto, as atividades ficam num segundo notch ao lado dele, cada uma com um botão para ir à janela e outro para dispensar.
- **Ctrl+Alt+J** (desligado por padrão) vai para a janela do que precisa de você e, apertando de novo, para a próxima.
- **Atualização mais segura.** Quando o Yata passar a ser assinado, ele só instala atualizações assinadas pelo mesmo editor. O instalador também confere a assinatura da Microsoft antes de instalar o .NET.
- **Correções:**
  - "ligar às 9h" vira lembrete;
  - o botão **Entrar** reconhece os links novos do Teams e os do Outlook com Safe Links;
  - uma falha de rede não deixa mais a música sem letra por 3 dias;
  - o botão de atualizar não trava mais em "Baixando…";
  - um agente que trabalha por muito tempo não some mais da bolha.

[Notas completas](https://github.com/akaneni/yata/releases/tag/v0.5.0)

## 0.4.0 — 2026-10-27

- **Do notch para o terminal do agente.** Quando o Claude Code, o Codex ou um script precisa de você, um clique na bolha traz a janela do terminal ou do editor em que ele roda. Você aprova ou nega na própria ferramenta: o Yata nunca decide pelo agente.
- **Configurações → Agentes de IA.** Liga e desliga os hooks do Claude Code, do Cursor e do Codex (inclusive no WSL) sem editar arquivos, com uma cópia de cada configuração antes de mexer.
- **Agora, sem esperar.** **Procurar agora** busca versão nova, o **↻** lê o Google na hora e **Atualizar agora** busca a previsão do tempo.
- A bolha não foge mais do mouse, e o notch abre quando o mouse para sobre ele.

[Notas completas](https://github.com/akaneni/yata/releases/tag/v0.4.0)

## 0.3.0 — 2026-10-27

- **Bolha ao lado do notch.** Com música tocando, a contagem do Pomodoro e a próxima reunião vão para um segundo notch pequeno, uma coisa por vez, por prioridade.
- **Atividades de outros programas.** O `yata-cli` deixa scripts, builds e downloads mostrarem o andamento na bolha.
- **Agentes de IA no notch.** Claude Code, Cursor e Codex aparecem trabalhando, esperando você e concluídos, uma bolha por sessão. Só o estado vai para o notch, nunca prompts, comandos ou respostas.
- **Avisos que não se perdem.** Lembretes que chegam com o notch aberto ou em tela cheia esperam e aparecem um de cada vez.

[Notas completas](https://github.com/akaneni/yata/releases/tag/v0.3.0)

## 0.2.1 — 2026-10-27

- **Atualizar e reiniciar.** Com o aviso de versão nova ligado, a atualização é um clique: o instalador vem do GitHub e é conferido com o SHA-256 publicado.
- **Tarefas do Google na hora.** Com a aba Tarefas aberta, o que muda no Google Tasks aparece em até uns 15 segundos.
- **Comprovação de origem.** Cada instalador tem um registro assinado pelo GitHub que o liga ao código que o gerou.

[Notas completas](https://github.com/akaneni/yata/releases/tag/v0.2.1)

## 0.2.0 — 2026-10-27

- **Inglês e português**, seguindo o idioma do Windows ou à sua escolha.
- **Configurações com barra lateral**, **Desfazer** ao remover tarefas ou limpar a prateleira, e menus de escolha.
- **Acessibilidade:** teclado nas Configurações, nomes para leitores de tela, menos movimento quando o Windows pede e mais contraste.
- **Instalador menor:** de 6 MB para cerca de 2,6 MB.

[Notas completas](https://github.com/akaneni/yata/releases/tag/v0.2.0)

## 0.1.1 — 2026-10-26

- **Google Agenda e Google Tasks**, que ficaram de fora da 0.1.0.

[Notas completas](https://github.com/akaneni/yata/releases/tag/v0.1.1)

## 0.1.0 — 2026-10-26

- Primeira versão: mídia de qualquer player com letras sincronizadas, avisos do sistema, câmera e microfone em uso, Foco, Água, Tarefas, Prateleira, Tempo e o Yata, o corvo que traz os lembretes.

[Notas completas](https://github.com/akaneni/yata/releases/tag/v0.1.0)
