# language: pt
Funcionalidade: Saga da Ordem de Serviço
  Para garantir consistência entre OS, cobrança e execução
  Como oficina
  Quero que a Saga orquestre os serviços e compense falhas

  Contexto:
    Dado uma OS aberta com 1 serviço de R$ 100,00 e 2 peças de R$ 20,00

  Cenário: Caminho feliz até a finalização
    Quando o orçamento é solicitado
    E o billing gera o orçamento
    E o cliente aprova o orçamento
    E o pagamento é confirmado
    E as peças são reservadas
    E a execução é finalizada
    Então a OS deve ser finalizada
    E nenhuma compensação deve ser disparada

  Cenário: Orçamento recusado
    Quando o orçamento é solicitado
    E o billing gera o orçamento
    E o cliente recusa o orçamento
    Então a Saga deve pedir o cancelamento do orçamento
    E quando o billing confirmar o cancelamento a OS deve ser cancelada

  Cenário: Pagamento recusado
    Quando o orçamento é solicitado
    E o billing gera o orçamento
    E o cliente aprova o orçamento
    E o pagamento é recusado
    Então a Saga deve pedir o cancelamento do orçamento
    E quando o billing confirmar o cancelamento a OS deve ser cancelada

  Cenário: Falta de peças estorna o pagamento
    Quando o orçamento é solicitado
    E o billing gera o orçamento
    E o cliente aprova o orçamento
    E o pagamento é confirmado
    E a reserva de peças falha por falta de estoque
    Então a Saga deve pedir o estorno do pagamento
    E quando o estorno, a liberação das peças e o cancelamento forem confirmados a OS deve ser cancelada

  Cenário: Falha na execução libera peças e estorna
    Quando o orçamento é solicitado
    E o billing gera o orçamento
    E o cliente aprova o orçamento
    E o pagamento é confirmado
    E as peças são reservadas
    E a execução falha
    Então a Saga deve pedir a liberação das peças
    E a Saga deve pedir o estorno do pagamento
    E quando todas as compensações forem confirmadas a OS deve ser cancelada