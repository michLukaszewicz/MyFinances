import { useState } from "react";
import Box from "../Box/Box";
import type { Transaction } from "../../Models/Transaction";

interface Props {
  onAddTransaction: (transaction: Transaction) => void;
}

const AddTransaction = ({ onAddTransaction }: Props) => {
  const [form, setForm] = useState({
    date: new Date().toISOString().split("T")[0],
    description: "",
    amount: "0.0",
    category: "Groceries",
    bankAccount: "account1",
    otherSideOfTransaction: "",
  });

  const onSubmit = (event: React.FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    onAddTransaction({ ...form, id: -1, date: new Date(form.date), amount: parseFloat(form.amount) });
    setForm({...form, date: new Date().toISOString().split("T")[0], description: "", amount: "0.0", category: "Groceries", otherSideOfTransaction: ""})
  };

  return (
    <Box header="Add Transaction">
      <form onSubmit={onSubmit} className="grid grid-cols-[2.4fr_0.9fr_0.5fr_1fr_0.5fr] px-2 items-center">
        <div className="min-w-0 flex flex-col gap-1">
          <input
            autoFocus
            type="text"
            placeholder={parseFloat(form.amount) < 0 ? "Transaction To" : "Transaction From"}
            value={form.otherSideOfTransaction}
            onChange={(e) => setForm({ ...form, otherSideOfTransaction: e.target.value })}
            className="text-blue-700 font-medium text-xl bg-transparent focus:outline-none"
          />

          <input
            type="text"
            placeholder="Description"
            value={form.description}
            onChange={(e) => setForm({ ...form, description: e.target.value })}
            className="text-s text-gray-500 bg-transparent focus:outline-none"
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
            className={`w-full text-[1.1rem] font-semibold text-right bg-transparent focus:outline-none
              ${parseFloat(form.amount) < 0 ? "text-red-600" : "text-green-600"}`}
          />
          <span className="text-xs text-gray-400">PLN</span>
        </div>
        <div className="justify-end ml-5">
              <button
                type="submit"
                className="py-2 px-5 bg-blue-600 text-white rounded-md hover:bg-blue-700 focus:outline-none focus:ring-2 focus:ring-blue-500 focus:ring-offset-2">
                Add
              </button>
        </div>
      </form>
    </Box>
  );
};

export default AddTransaction;
