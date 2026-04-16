# Privacidade e LGPD - Smart Gallery AWS

## Escopo

Este documento define regras minimas de privacidade para uso da API Smart Gallery AWS.

## Dados tratados

- Imagem enviada pelo usuario.
- Metadados tecnicos da imagem (formato, tamanho, dimensoes, data).
- Tags manuais e tags geradas por IA (Rekognition).

## Regras de privacidade

- O upload e privado por padrao (`publica=false`).
- Apenas imagens publicas sao retornadas por listagem, busca e detalhe.
- O campo `UsuarioId` nao e exposto no DTO publico de detalhe.
- URLs assinadas nao devem ser versionadas com query string sensivel.

## Consentimento

Antes do upload, o usuario deve confirmar que:

- possui direito de uso da imagem;
- nao viola direitos de terceiros;
- tem base legal para tratamento quando houver dados pessoais na imagem.

## Retencao e exclusao

- O usuario pode remover imagens via endpoint de delecao.
- Artefatos de evidencia devem registrar apenas URLs sem assinatura.

## Boas praticas operacionais

- Nao publicar logs com tokens temporarios AWS.
- Restringir CORS para origens explicitamente permitidas.
- Manter mensagens de erro genericas para clientes externos.
