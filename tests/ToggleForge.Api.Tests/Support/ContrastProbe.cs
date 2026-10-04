using System.Text.Json;
using Microsoft.Playwright;

namespace ToggleForge.Api.Tests.Support;

/// <summary>
/// Mede no navegador a razão de contraste WCAG 2 (<c>(L1 + 0.05) / (L2 + 0.05)</c>) entre a cor de um elemento e o
/// fundo que aparece atrás dele, com as cores que o Chromium calculou de fato.
/// </summary>
/// <remarks>
/// O Tailwind 4 escreve as cores em <c>oklch</c>, que o <c>getComputedStyle</c> devolve como está. Em vez de converter
/// à mão, o script pinta a cor num pixel de <c>canvas</c> e lê o sRGB que o próprio navegador desenhou. O fundo
/// efetivo é o primeiro ancestral com fundo opaco, com os fundos translúcidos do caminho compostos por cima dele.
/// </remarks>
public static class ContrastProbe
{
    private const int MaxTabStops = 60;

    private const string Library = """
        const probe = (() => {
          const canvas = document.createElement('canvas');
          canvas.width = canvas.height = 1;
          const context = canvas.getContext('2d', { willReadFrequently: true });
          const rgba = color => {
            context.clearRect(0, 0, 1, 1);
            context.fillStyle = '#000';
            context.fillStyle = color;
            context.fillRect(0, 0, 1, 1);
            const [r, g, b, a] = context.getImageData(0, 0, 1, 1).data;
            return a === 0 ? [0, 0, 0, 0] : [r * 255 / a, g * 255 / a, b * 255 / a, a / 255];
          };
          const over = (top, bottom) => [0, 1, 2].map(i => top[i] * top[3] + bottom[i] * (1 - top[3])).concat(1);
          const background = element => {
            const layers = [];
            for (let node = element; node instanceof Element; node = node.parentElement) {
              const color = rgba(getComputedStyle(node).backgroundColor);
              if (color[3] > 0) {
                layers.push(color);
              }
              if (color[3] >= 1) {
                break;
              }
            }
            return layers.reduceRight((below, layer) => over(layer, below), [255, 255, 255, 1]);
          };
          const luminance = ([r, g, b]) => {
            const [R, G, B] = [r, g, b].map(c => {
              const s = c / 255;
              return s <= 0.04045 ? s / 12.92 : Math.pow((s + 0.055) / 1.055, 2.4);
            });
            return 0.2126 * R + 0.7152 * G + 0.0722 * B;
          };
          const ratio = (a, b) => {
            const [light, dark] = [luminance(a), luminance(b)].sort((x, y) => y - x);
            return (light + 0.05) / (dark + 0.05);
          };
          const behind = element => background(element.parentElement ?? element);
          const borderRatio = element => {
            const outside = behind(element);
            return ratio(over(rgba(getComputedStyle(element).borderTopColor), outside), outside);
          };
          const textRatio = element => {
            const back = background(element);
            return ratio(over(rgba(getComputedStyle(element).color), back), back);
          };
          const describe = element =>
            element.tagName.toLowerCase() + (element.id ? '#' + element.id : '') +
            (element.getAttribute('aria-label') ? '[' + element.getAttribute('aria-label') + ']' : '') +
            (element.textContent.trim() ? ' "' + element.textContent.trim().slice(0, 30) + '"' : '');
          return { borderRatio, textRatio, describe };
        })();
        """;

    /// <summary>A razão entre a borda de cada campo visível da página e o fundo atrás dele.</summary>
    /// <remarks>
    /// Rádios e checkboxes ficam de fora: o Chromium desenha os nativos com as cores dele, sem usar a borda do CSS, e
    /// a cor computada não diria o que aparece na tela.
    /// </remarks>
    public static async Task<IReadOnlyList<ControlContrast>> FieldBordersAsync(IPage page)
    {
        var measured = await page.EvaluateAsync<string>($$"""
            () => {
              {{Library}}
              return JSON.stringify([...document.querySelectorAll('input:not([type=checkbox]):not([type=radio]):not([type=hidden]), select, textarea')]
                .filter(element => element.getClientRects().length > 0)
                .map(element => ({ control: probe.describe(element), ratio: probe.borderRatio(element) })));
            }
            """);
        return Parse<ControlContrast[]>(measured);
    }

    /// <summary>A razão entre a borda do elemento e o fundo atrás dele.</summary>
    public static Task<double> BorderRatioAsync(ILocator element) =>
        element.EvaluateAsync<double>($$"""element => { {{Library}} return probe.borderRatio(element); }""");

    /// <summary>A razão entre a cor do texto do elemento e o fundo dele.</summary>
    public static Task<double> TextRatioAsync(ILocator element) =>
        element.EvaluateAsync<double>($$"""element => { {{Library}} return probe.textRatio(element); }""");

    /// <summary>Tab a partir do começo da página até o foco sair dela ou voltar a um elemento já visto.</summary>
    /// <returns>O contorno de foco de cada parada, na ordem do Tab.</returns>
    /// <remarks>
    /// Um <c>blur</c> não basta: o Chromium continua o Tab de onde o foco estava, e as paradas antes dele (a chave, no
    /// formulário com JSON escolhido) ficariam de fora. O ponto de partida é um elemento fora da ordem do Tab
    /// (<c>tabindex=-1</c>) no começo do <c>body</c>.
    /// </remarks>
    public static async Task<IReadOnlyList<FocusOutline>> TabStopsAsync(IPage page)
    {
        await page.EvaluateAsync("""
            () => {
              const start = document.createElement('span');
              start.tabIndex = -1;
              document.body.prepend(start);
              start.focus();
            }
            """);
        var stops = new List<FocusOutline>();
        for (var tab = 0; tab < MaxTabStops; tab++)
        {
            await page.Keyboard.PressAsync("Tab");
            if (await ActiveOutlineAsync(page) is not { Seen: false } stop)
            {
                return stops;
            }

            stops.Add(stop);
        }

        throw new InvalidOperationException(
            $"Tab did not leave the page after {MaxTabStops} stops: {string.Join(", ", stops.Select(stop => stop.Element))}");
    }

    /// <summary>
    /// O contorno de foco do elemento ativo. Num rádio que esconde o próprio contorno, o do cartão (<c>label</c>) que
    /// o contém, que mostra o foco inteiro (os tipos do "New flag"); os outros rádios mostram o contorno global.
    /// </summary>
    /// <returns>Nulo quando o foco está no <c>body</c> (o Tab saiu da página).</returns>
    /// <remarks>Guarda na página os elementos já vistos, para dizer quando o Tab deu a volta.</remarks>
    private static async Task<FocusOutline?> ActiveOutlineAsync(IPage page) =>
        JsonSerializer.Deserialize<FocusOutline?>(await page.EvaluateAsync<string>($$"""
            () => {
              {{Library}}
              const active = document.activeElement;
              if (!active || active === document.body) {
                return 'null';
              }
              window.__tabStops ??= new Set();
              const seen = window.__tabStops.has(active);
              window.__tabStops.add(active);
              const hidesOwn = active.matches('input[type=radio]') && getComputedStyle(active).outlineStyle === 'none';
              const shown = hidesOwn ? active.closest('label') ?? active : active;
              const style = getComputedStyle(shown);
              return JSON.stringify({ element: probe.describe(active), style: style.outlineStyle, width: parseFloat(style.outlineWidth), seen });
            }
            """), JsonSerializerOptions.Web);

    // O Playwright só desserializa tipos com construtor sem parâmetros; o System.Text.Json aceita os records.
    private static T Parse<T>(string json) =>
        JsonSerializer.Deserialize<T>(json, JsonSerializerOptions.Web) ?? throw new InvalidOperationException($"The probe returned {json}.");
}

/// <param name="Control">O campo, com tag, id e texto, para a mensagem de falha.</param>
public sealed record ControlContrast(string Control, double Ratio);

/// <param name="Element">O elemento focado, para a mensagem de falha.</param>
/// <param name="Width">Em pixels CSS.</param>
/// <param name="Seen">O elemento já tinha sido focado nesta página: o Tab deu a volta.</param>
public sealed record FocusOutline(string Element, string Style, double Width, bool Seen);
