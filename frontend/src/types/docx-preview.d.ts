declare module 'docx-preview' {
  export interface RenderOptions {
    className?: string;
    inWrapper?: boolean;
    ignoreWidth?: boolean;
    ignoreHeight?: boolean;
    ignoreFonts?: boolean;
    breakPages?: boolean;
    ignoreLastRenderedPageBreak?: boolean;
    experimental?: boolean;
    trimXmlDeclaration?: boolean;
    debug?: boolean;
  }

  export function renderAsync(
    data: Blob | ArrayBuffer | Uint8Array,
    container: HTMLElement,
    styleContainer?: HTMLElement | null | undefined,
    options?: RenderOptions
  ): Promise<void>;
}
