// pulled from https://github.com/types/npm-jsonld - MIT license, Copyright (c) 2016 Blake Embrey (hello@blakeembrey.com)

export interface JsonLdObject {
  [key: string]: JsonLdPrimitive | JsonLdPrimitive[];
}

export type JsonLdPrimitive = string | number | boolean | JsonLd;

export type JsonLd = JsonLdObject | JsonLdObject[];

export interface JsonLdCallback {
  (err: Error | null, success: JsonLd): void;
}

export interface DocumentObject {
  contextUrl?: string;
  document: string;
  documentUrl: string;
}

export interface DocumentLoader {
  (url: string, callback: (err: Error | null, documentObject: DocumentObject) => void): void;
}

export interface NormalizeOptions {
  algorithm?: string;
  base?: string;
  expandContext?: any;
  inputFormat?: string;
  format?: string;
  documentLoader?: DocumentLoader;
}

export interface CompactOptions {
  base?: string;
  compactArrays?: boolean;
  graph?: boolean;
  expandContext?: any;
  skipExpansion?: boolean;
  documentLoader?: DocumentLoader;
}

export interface ExpandOptions {
  base?: string;
  expandContext?: any;
  keepFreeFloatingNodes?: boolean;
  documentLoader?: DocumentLoader;
}

export interface FlattenOptions {
  base?: string;
  expandContext?: any;
  documentLoader?: DocumentLoader;
}

export interface FrameOptions {
  base?: string;
  expandContext?: any;
  embed?: EmbedEnum;
  explicit?: boolean;
  requireAll?: boolean;
  omitDefault?: boolean;
  documentLoader?: DocumentLoader;
}

export type EmbedEnum = '@last' | '@always' | '@never' | '@link';

export interface ToRdfOptions {
  base?: string;
  expandContext?: any;
  format?: string;
  produceGeneralizedRdf?: boolean;
  documentLoader?: DocumentLoader;
}

export interface FromRdfOptions {
  rdfParser?: any;
  format?: string;
  useRdfType?: boolean;
  useNativeTypes?: boolean;
  documentLoader?: DocumentLoader;
}

export type RDFParser = ((input: string) => JsonLd) | ((input: string, callback: (err: Error | null, dataset: JsonLd) => void) => void);

/**
 * Compact a document according to a particular context.
 * See: http://json-ld.org/spec/latest/json-ld/#compacted-document-form
 */
export function compact (doc: JsonLd | string, context: JsonLd | string, callback: JsonLdCallback): void;
export function compact (doc: JsonLd | string, context: JsonLd | string, options: CompactOptions, callback: JsonLdCallback): void;

/**
 * Expand a document, removing its context.
 * See: http://json-ld.org/spec/latest/json-ld/#expanded-document-form
 */
export function expand (compacted: JsonLd | string, callback: JsonLdCallback): void;
export function expand (compacted: JsonLd | string, options: ExpandOptions, callback: JsonLdCallback): void;

/**
 * Flatten a document.
 * See: http://json-ld.org/spec/latest/json-ld/#flattened-document-form
 */
export function flatten (doc: JsonLd, callback: JsonLdCallback): void;
export function flatten (doc: JsonLd, options: FlattenOptions, callback: JsonLdCallback): void;

/**
 * Frame a document.
 * See: http://json-ld.org/spec/latest/json-ld-framing/#introduction
 */
export function frame (doc: JsonLd, frame: JsonLd, callback: JsonLdCallback): void;
export function frame (doc: JsonLd, frame: JsonLd, options: FrameOptions, callback: JsonLdCallback): void;

/**
 * Normalize a document using the RDF Dataset Normalization Algorithm (URDNA2015).
 * See: http://json-ld.github.io/normalization/spec/
 */
export function normalize (doc: JsonLd, callback: JsonLdCallback): void;
export function normalize (doc: JsonLd, options: NormalizeOptions, callback: JsonLdCallback): void;

/**
 * Serialize a document to N-Quads (RDF).
 */
export function toRDF (doc: JsonLd, callback: JsonLdCallback): void;
export function toRDF (doc: JsonLd, options: ToRdfOptions, callback: JsonLdCallback): void;

/**
 * Deserialize N-Quads (RDF) to JSON-LD.
 */
export function fromRDF (nquads: string, callback: JsonLdCallback): void;
export function fromRDF (nquads: string, options: FromRdfOptions, callback: JsonLdCallback): void;

/**
 * Register a custom async-callback-based RDF parser.
 */
export function registerRDFParser (contentType: string, parser: RDFParser): void;

export const promises: JsonLdProcessor;

export declare class JsonLdProcessor {
  /**
   * Compact a document according to a particular context.
   * See: http://json-ld.org/spec/latest/json-ld/#compacted-document-form
   */
  compact (doc: JsonLd | string, context: JsonLd | string, options?: CompactOptions): Promise<JsonLd>;

  /**
   * Expand a document, removing its context.
   * See: http://json-ld.org/spec/latest/json-ld/#expanded-document-form
   */
  expand (compacted: JsonLd | string, options?: ExpandOptions): Promise<JsonLd>;

  /**
   * Flatten a document.
   * See: http://json-ld.org/spec/latest/json-ld/#flattened-document-form
   */
  flatten (doc: JsonLd, options?: FlattenOptions): Promise<JsonLd>;

  /**
   * Frame a document.
   * See: http://json-ld.org/spec/latest/json-ld-framing/#introduction
   */
  frame (doc: JsonLd, frame: JsonLd, options?: FrameOptions): Promise<JsonLd>;

  /**
   * Normalize a document using the RDF Dataset Normalization Algorithm (URDNA2015).
   * See: http://json-ld.github.io/normalization/spec/
   */
  normalize (doc: JsonLd, options?: NormalizeOptions): Promise<JsonLd>;

  /**
   * Serialize a document to N-Quads (RDF).
   */
  toRDF (doc: JsonLd, options?: ToRdfOptions): Promise<string>;

  /**
   * Deserialize N-Quads (RDF) to JSON-LD.
   */
  fromRDF (nquads: string, options?: FromRdfOptions): Promise<JsonLd>;
}
