import { useState } from "react";
import type { Transaction } from "../../Models/Transaction";

interface Props {
  onSubmitAction: (transaction: Transaction) => void;
  children: React.ReactNode;
  basedOnTransaction?: Transaction;
}

type TransactionDto = {
  id: number;
  date: string;
  description: string;
  amount: string;
  category: string;
  bankAccount: string;
  otherSideOfTransaction: string;
};

const defaultState: TransactionDto = {
  id: -1,
  date: new Date().toISOString().split("T")[0],
  description: "",
  amount: "0.0",
  category: "Groceries",
  bankAccount: "account1",
  otherSideOfTransaction: "",
};

const mapTransaction = (transaction: Transaction): TransactionDto => ({
  ...transaction,
  date: transaction.date.toISOString().split("T")[0],
  amount: transaction.amount.toString(),
  id: transaction.id
});

const TransactionForm = ({ onSubmitAction, basedOnTransaction, children }: Props) => {
  const [form, setForm] = useState(basedOnTransaction ? mapTransaction(basedOnTransaction) : defaultState);

  const onSubmit = (event: React.FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    onSubmitAction({ ...form, date: new Date(form.date), amount: parseFloat(form.amount) });
    setForm({
      ...form,
      date: new Date().toISOString().split("T")[0],
      description: "",
      amount: "0.0",
      category: "Groceries",
      otherSideOfTransaction: "",
    });
  };

  return (
    <form onSubmit={onSubmit} className="grid grid-cols-[2.4fr_0.9fr_0.5fr_1fr_0.5fr] px-2 items-center">
      <div className="min-w-0 flex flex-col gap-1">
        <input
          type="text"
          placeholder={parseFloat(form.amount) < 0 ? "Transaction To" : "Transaction From"}
          value={form.otherSideOfTransaction}
          onChange={(e) => setForm({ ...form, otherSideOfTransaction: e.target.value })}
          className="text-blue-700 font-medium text-xl focus:outline-none border-b border-blue-100 focus:border-blue-400 transition-colors w-80"
        />

        <input
          type="text"
          placeholder="Description"
          value={form.description}
          onChange={(e) => setForm({ ...form, description: e.target.value })}
          className="text-s text-gray-500 bg-transparent focus:outline-none border-b border-blue-100 focus:border-blue-400 transition-colors w-80"
        />
      </div>
      <select
        aria-label="Category"
        value={form.category}
        onChange={(e) => setForm({ ...form, category: e.target.value })}
        className="bg-transparent focus:outline-none text-gray-500 w-23">
        <option value="Groceries">Groceries</option>
        <option value="Rent">Rent</option>
        <option value="Bills">Bills</option>
        <option value="Salary">Salary</option>
        <option value="Entertainment">Entertainment</option>
      </select>
      <div className="flex flex-col gap-2 text-xs text-gray-600 ">
        <input
          aria-label="Date"
          type="date"
          value={form.date}
          onChange={(e) => setForm({ ...form, date: e.target.value })}
          className="text-blue-700 w-27 text-[1rem] bg-transparent focus:outline-none [&::-webkit-calendar-picker-indicator]:cursor-pointer  [&::-webkit-calendar-picker-indicator]:invert"
        />
        <select
          aria-label="Bank Account"
          value={form.bankAccount}
          onChange={(e) => setForm({ ...form, bankAccount: e.target.value })}
          className="bg-transparent text-[0.9rem] w-20 self-center focus:outline-none">
          <option value="account1">Account 1</option>
          <option value="account2">Account 2</option>
        </select>
      </div>
      <div className="flex flex-col items-end text-right overflow-hidden">
        <input
          type="number"
          step="0.01"
          placeholder="0.00"
          value={form.amount}
          onChange={(e) => setForm({ ...form, amount: e.target.value })}
          className={`w-30 text-[1.1rem] font-semibold text-right bg-transparent focus:outline-none border-1 border-blue-100 rounded-md focus:border-blue-400
              ${parseFloat(form.amount) < 0 ? "text-red-600" : "text-green-600"}`}
        />
        <span className="text-xs text-gray-400">PLN</span>
      </div>
      <div className="justify-end ml-5">{children}</div>
    </form>
  );
};

export default TransactionForm;
