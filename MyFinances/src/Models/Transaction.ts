export interface Transaction {
    id: number;
    date: Date;
    description: string;
    amount: number;
    category: string;
    bankAccount: string;
    otherSideOfTransaction: string;
}