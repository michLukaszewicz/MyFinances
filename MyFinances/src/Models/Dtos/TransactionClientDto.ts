import type { Transaction } from "../Transaction";

export type ClientTransaction = Transaction & { clientId: string };