export interface Transaction {
    id: number;
    date: Date;
    description: string;
    amount: number;
    type: "income" | "expense";
    category: string;
    account: string;
    otherAccount: string;
}